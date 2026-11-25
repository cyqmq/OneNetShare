using System;
using System.Reflection;
using System.Threading;

namespace WifiHotspot
{
    internal enum MobileHotspotState
    {
        Unavailable,
        Off,
        On,
        InTransition,
        Error
    }

    internal static class MobileHotspotManager
    {
        private static readonly Type NetworkInformationType = GetWinRTType("Windows.Networking.Connectivity.NetworkInformation, Windows.Networking.Connectivity, ContentType=WindowsRuntime");
        private static readonly Type ManagerType = GetWinRTType("Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager, Windows.Networking.NetworkOperators, ContentType=WindowsRuntime");
        private static readonly Type ResultType = GetWinRTType("Windows.Networking.NetworkOperators.NetworkOperatorTetheringOperationResult, Windows.Networking.NetworkOperators, ContentType=WindowsRuntime");
        private static readonly Type ConfigType = GetWinRTType("Windows.Networking.NetworkOperators.NetworkOperatorTetheringAccessPointConfiguration, Windows.Networking.NetworkOperators, ContentType=WindowsRuntime");
        private static readonly Type ExtensionType = GetWinRTType("System.WindowsRuntimeSystemExtensions, System.Runtime.WindowsRuntime");

        internal static bool IsAvailable
        {
            get { return NetworkInformationType != null && ManagerType != null && ResultType != null && ConfigType != null && ExtensionType != null; }
        }

        internal static MobileHotspotState GetState(out string detail)
        {
            detail = string.Empty;
            try
            {
                object manager = GetManager();
                if (manager == null)
                {
                    detail = "未找到可用的网络连接配置文件";
                    return MobileHotspotState.Unavailable;
                }
                return GetState(manager, out detail);
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return MobileHotspotState.Error;
            }
        }

        internal static HotspotResult Start(string ssid, string password)
        {
            return RunOnBackgroundThread(delegate
            {
                object manager = GetManager();
                if (manager == null)
                    return new HotspotResult { Success = false, Message = "未找到可用的网络连接配置文件，无法启动移动热点" };

                string stateDetail;
                MobileHotspotState state = GetState(manager, out stateDetail);
                if (state == MobileHotspotState.On || state == MobileHotspotState.InTransition)
                    AwaitAction(InvokeMethod(manager, "StopTetheringAsync", 0), 15000);

                ConfigureAccessPoint(manager, ssid, password);
                object result = AwaitOperation(InvokeMethod(manager, "StartTetheringAsync", 0), ResultType, 20000);
                return ToResult(result, "启动移动热点");
            });
        }

        internal static HotspotResult Stop()
        {
            return RunOnBackgroundThread(delegate
            {
                object manager = GetManager();
                if (manager == null)
                    return new HotspotResult { Success = false, Message = "未找到可用的网络连接配置文件，无法停止移动热点" };

                string stateDetail;
                MobileHotspotState state = GetState(manager, out stateDetail);
                if (state == MobileHotspotState.Off)
                    return new HotspotResult { Success = true, Message = "移动热点已关闭" };

                object result = AwaitOperation(InvokeMethod(manager, "StopTetheringAsync", 0), ResultType, 15000);
                return ToResult(result, "停止移动热点");
            });
        }

        private static HotspotResult RunOnBackgroundThread(Func<HotspotResult> action)
        {
            HotspotResult result = null;
            Exception error = null;
            var thread = new Thread(delegate()
            {
                try { result = action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.IsBackground = true;
            try { thread.SetApartmentState(ApartmentState.MTA); }
            catch { }
            thread.Start();
            if (!thread.Join(45000))
                return new HotspotResult { Success = false, Message = "调用 Windows 移动热点 API 超时" };
            if (error != null)
                return new HotspotResult { Success = false, Message = error.Message };
            return result ?? new HotspotResult { Success = false, Message = "Windows 移动热点 API 未返回结果" };
        }

        private static Type GetWinRTType(string name)
        {
            try { return Type.GetType(name, false); }
            catch { return null; }
        }

        private static object GetManager()
        {
            if (!IsAvailable) return null;
            MethodInfo getProfile = FindMethod(NetworkInformationType, "GetInternetConnectionProfile", 0, true);
            if (getProfile == null) return null;
            object profile = getProfile.Invoke(null, null);
            if (profile == null) return null;
            MethodInfo create = FindMethod(ManagerType, "CreateFromConnectionProfile", 1, true);
            if (create == null) return null;
            return create.Invoke(null, new[] { profile });
        }

        private static MobileHotspotState GetState(object manager, out string detail)
        {
            detail = string.Empty;
            PropertyInfo property = manager.GetType().GetProperty("TetheringOperationalState");
            if (property == null)
            {
                detail = "无法读取移动热点状态";
                return MobileHotspotState.Error;
            }
            object value = property.GetValue(manager, null);
            if (value == null)
            {
                detail = "移动热点状态为空";
                return MobileHotspotState.Error;
            }

            string text = value.ToString();
            detail = "移动热点状态: " + text;
            int number;
            if (!int.TryParse(text, out number)) number = -1;
            if (number == 1 || text == "On") return MobileHotspotState.On;
            if (number == 0 || text == "Off") return MobileHotspotState.Off;
            if (number == 2 || text == "InTransition") return MobileHotspotState.InTransition;
            return MobileHotspotState.Error;
        }

        private static void ConfigureAccessPoint(object manager, string ssid, string password)
        {
            object config = Activator.CreateInstance(ConfigType);
            ConfigType.GetProperty("Ssid").SetValue(config, ssid, null);
            ConfigType.GetProperty("Passphrase").SetValue(config, password, null);
            object action = InvokeMethod(manager, "ConfigureAccessPointAsync", 1, config);
            AwaitAction(action, 15000);
        }

        private static object InvokeMethod(object target, string name, int parameterCount)
        {
            MethodInfo method = FindMethod(target.GetType(), name, parameterCount, false);
            if (method == null)
                throw new MissingMethodException(target.GetType().FullName, name);
            return method.Invoke(target, null);
        }

        private static object InvokeMethod(object target, string name, int parameterCount, object parameter)
        {
            MethodInfo method = FindMethod(target.GetType(), name, parameterCount, false);
            if (method == null)
                throw new MissingMethodException(target.GetType().FullName, name);
            return method.Invoke(target, new[] { parameter });
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount, bool staticMethod)
        {
            BindingFlags flags = BindingFlags.Public | (staticMethod ? BindingFlags.Static : BindingFlags.Instance);
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.Name == name && method.GetParameters().Length == parameterCount)
                    return method;
            }
            return null;
        }

        private static MethodInfo FindAsTask(bool generic)
        {
            foreach (MethodInfo method in ExtensionType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "AsTask") continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1) continue;
                if (generic && method.IsGenericMethodDefinition && parameters[0].ParameterType.Name == "IAsyncOperation`1")
                    return method;
                if (!generic && !method.IsGenericMethod && parameters[0].ParameterType.Name == "IAsyncAction")
                    return method;
            }
            return null;
        }

        private static object AwaitOperation(object asyncOperation, Type resultType, int timeoutMs)
        {
            MethodInfo asTask = FindAsTask(true);
            if (asTask == null) throw new InvalidOperationException("找不到 WinRT AsTask(IAsyncOperation) 方法");
            object task = asTask.MakeGenericMethod(resultType).Invoke(null, new[] { asyncOperation });
            WaitTask(task, timeoutMs);
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }

        private static void AwaitAction(object asyncAction, int timeoutMs)
        {
            MethodInfo asTask = FindAsTask(false);
            if (asTask == null) throw new InvalidOperationException("找不到 WinRT AsTask(IAsyncAction) 方法");
            object task = asTask.Invoke(null, new[] { asyncAction });
            WaitTask(task, timeoutMs);
        }

        private static void WaitTask(object task, int timeoutMs)
        {
            MethodInfo wait = task.GetType().GetMethod("Wait", new[] { typeof(int) });
            if (wait == null) throw new InvalidOperationException("无法等待 WinRT 异步操作");
            bool completed = (bool)wait.Invoke(task, new object[] { timeoutMs });
            if (!completed) throw new TimeoutException("WinRT 异步操作超时");
        }

        private static HotspotResult ToResult(object result, string operation)
        {
            PropertyInfo statusProperty = result.GetType().GetProperty("Status");
            PropertyInfo errorProperty = result.GetType().GetProperty("AdditionalErrorMessage");
            string status = statusProperty == null ? "Unknown" : statusProperty.GetValue(result, null).ToString();
            string extra = errorProperty == null ? string.Empty : errorProperty.GetValue(result, null) as string;
            if (status == "Success")
                return new HotspotResult { Success = true, Message = operation + "成功" };
            return new HotspotResult
            {
                Success = false,
                Message = operation + "失败: " + DescribeStatus(status) + (string.IsNullOrEmpty(extra) ? string.Empty : "\n" + extra)
            };
        }

        private static string DescribeStatus(string status)
        {
            switch (status)
            {
                case "WlanRadioOff": return "WlanRadioOff（无线射频关闭，请关闭飞行模式并打开 Wi-Fi）";
                case "BluetoothRadioOff": return "BluetoothRadioOff（蓝牙射频关闭）";
                case "InternetConnectionUnavailable": return "InternetConnectionUnavailable（当前没有可用的互联网连接）";
                case "MobileBroadbandAccountNotProvisioned": return "MobileBroadbandAccountNotProvisioned（未配置移动宽带账户）";
                case "DeviceNotSupported": return "DeviceNotSupported（设备或驱动不支持移动热点）";
                default: return status;
            }
        }
    }
}
