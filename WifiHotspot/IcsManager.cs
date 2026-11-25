using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WifiHotspot
{
    public class IcsManager
    {
        public class NetworkAdapterInfo
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public bool IsActive { get; set; }
            public string StatusText { get; set; }

            public override string ToString()
            {
                return string.Format("{0}  [{1}]", Name, StatusText);
            }
        }

        [ComImport]
        [Guid("00020404-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumVARIANT
        {
            [PreserveSig]
            int Next(int celt, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] object[] rgvar, out int pceltFetched);
            [PreserveSig]
            int Skip(int celt);
            [PreserveSig]
            int Reset();
            [PreserveSig]
            int Clone(out IEnumVARIANT ppenum);
        }

        private const int ICSSHARINGTYPE_PUBLIC = 0;
        private const int ICSSHARINGTYPE_PRIVATE = 1;

        private object _manager;

        private object GetManager()
        {
            if (_manager != null) return _manager;

            Type type = Type.GetTypeFromProgID("HNetCfg.HNetShare.1")
                        ?? Type.GetTypeFromProgID("HNetCfg.HNetShare");
            if (type == null)
                throw new InvalidOperationException("无法找到 HNetCfg.HNetShare COM 组件。请确认系统已启用 Internet Connection Sharing 服务。");

            _manager = Activator.CreateInstance(type);
            return _manager;
        }

        private static object Invoke(object target, string member, params object[] args)
        {
            return target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
        }

        private static object GetProp(object target, string name)
        {
            return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);
        }

        private static List<object> EnumEveryConnection(object manager)
        {
            var result = new List<object>();
            object collection = Invoke(manager, "EnumEveryConnection");
            int count = Convert.ToInt32(GetProp(collection, "Count"));
            object enumerator = GetProp(collection, "_NewEnum");

            bool usedVariant = false;
            try
            {
                var variant = (IEnumVARIANT)enumerator;
                var buffer = new object[1];
                int fetched;
                while (result.Count < count && variant.Next(1, buffer, out fetched) == 0 && fetched > 0)
                    result.Add(buffer[0]);
                usedVariant = result.Count > 0 || count == 0;
            }
            catch
            {
                usedVariant = false;
            }

            if (!usedVariant)
            {
                result.Clear();
                for (int i = 0; i < count; i++)
                {
                    bool moved = (bool)enumerator.GetType().InvokeMember("MoveNext", BindingFlags.InvokeMethod, null, enumerator, null);
                    if (!moved) break;
                    PropertyInfo[] props = enumerator.GetType().GetProperties();
                    if (props.Length > 0)
                        result.Add(props[0].GetValue(enumerator, null));
                }
            }

            Release(collection);
            Release(enumerator);
            return result;
        }

        private static void Release(object obj)
        {
            if (obj != null && Marshal.IsComObject(obj))
                Marshal.ReleaseComObject(obj);
        }

        public List<NetworkAdapterInfo> GetNetworkAdapters()
        {
            var list = new List<NetworkAdapterInfo>();
            try
            {
                object manager = GetManager();
                foreach (var conn in EnumEveryConnection(manager))
                {
                    try
                    {
                        object props = Invoke(manager, "get_NetConnectionProps", conn);
                        string guid = GetProp(props, "Guid") as string;
                        string name = GetProp(props, "Name") as string;
                        string device = GetProp(props, "DeviceName") as string;
                        int status = Convert.ToInt32(GetProp(props, "Status"));

                        list.Add(new NetworkAdapterInfo
                        {
                            Id = guid,
                            Name = string.IsNullOrEmpty(name) ? device : name,
                            Description = device ?? string.Empty,
                            IsActive = status == 5,
                            StatusText = status == 5 ? "已连接" : status == 6 ? "已断开" : "状态 " + status
                        });
                        Release(props);
                    }
                    finally
                    {
                        Release(conn);
                    }
                }
            }
            catch (Exception)
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    list.Add(new NetworkAdapterInfo
                    {
                        Id = ni.Id,
                        Name = ni.Name,
                        Description = ni.Description,
                        IsActive = ni.OperationalStatus == OperationalStatus.Up,
                        StatusText = ni.OperationalStatus.ToString()
                    });
                }
            }
            return list;
        }

        public HotspotResult EnableSharing(string publicAdapterId, string privateAdapterId)
        {
            if (string.IsNullOrEmpty(publicAdapterId) || string.IsNullOrEmpty(privateAdapterId))
                return new HotspotResult { Success = false, Message = "公共/专用适配器 ID 不能为空" };

            try
            {
                object manager = GetManager();
                var connections = EnumEveryConnection(manager);

                object publicConfig = null, privateConfig = null;
                object publicConn = null, privateConn = null;

                foreach (var conn in connections)
                {
                    object props = Invoke(manager, "get_NetConnectionProps", conn);
                    string guid = GetProp(props, "Guid") as string;
                    Release(props);

                    if (guid == publicAdapterId)
                    {
                        publicConn = conn;
                        publicConfig = Invoke(manager, "get_INetSharingConfigurationForINetConnection", conn);
                    }
                    else if (guid == privateAdapterId)
                    {
                        privateConn = conn;
                        privateConfig = Invoke(manager, "get_INetSharingConfigurationForINetConnection", conn);
                    }
                    else
                    {
                        Release(conn);
                    }
                }

                if (publicConfig == null || privateConfig == null)
                {
                    Release(publicConfig);
                    Release(privateConfig);
                    Release(publicConn);
                    Release(privateConn);
                    return new HotspotResult { Success = false, Message = "未找到指定的网络适配器" };
                }

                Invoke(publicConfig, "EnableSharing", ICSSHARINGTYPE_PUBLIC);
                Invoke(privateConfig, "EnableSharing", ICSSHARINGTYPE_PRIVATE);

                Release(publicConfig);
                Release(privateConfig);
                Release(publicConn);
                Release(privateConn);
                return new HotspotResult { Success = true, Message = "ICS 共享已启用" };
            }
            catch (Exception ex)
            {
                return new HotspotResult { Success = false, Message = ex.Message };
            }
        }

        public List<string> GetSharingAdapters()
        {
            var ids = new List<string>();
            try
            {
                object manager = GetManager();
                foreach (var conn in EnumEveryConnection(manager))
                {
                    try
                    {
                        object config = Invoke(manager, "get_INetSharingConfigurationForINetConnection", conn);
                        bool enabled = Convert.ToBoolean(GetProp(config, "SharingEnabled"));
                        if (enabled)
                        {
                            object props = Invoke(manager, "get_NetConnectionProps", conn);
                            string guid = GetProp(props, "Guid") as string;
                            if (!string.IsNullOrEmpty(guid)) ids.Add(guid);
                            Release(props);
                        }
                        Release(config);
                    }
                    finally
                    {
                        Release(conn);
                    }
                }
            }
            catch (Exception)
            {
                // ignore
            }
            return ids;
        }

        public HotspotResult DisableSharing(string adapterId)
        {
            try
            {
                object manager = GetManager();
                foreach (var conn in EnumEveryConnection(manager))
                {
                    try
                    {
                        object props = Invoke(manager, "get_NetConnectionProps", conn);
                        string guid = GetProp(props, "Guid") as string;
                        Release(props);

                        if (guid == adapterId)
                        {
                            object config = Invoke(manager, "get_INetSharingConfigurationForINetConnection", conn);
                            Invoke(config, "DisableSharing");
                            Release(config);
                            return new HotspotResult { Success = true, Message = "已关闭共享" };
                        }
                    }
                    finally
                    {
                        Release(conn);
                    }
                }
                return new HotspotResult { Success = false, Message = "未找到适配器" };
            }
            catch (Exception ex)
            {
                return new HotspotResult { Success = false, Message = ex.Message };
            }
        }

        ~IcsManager()
        {
            Release(_manager);
        }
    }
}
