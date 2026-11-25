using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WifiHotspot
{
    public class ProxySettings
    {
        public bool Enabled { get; set; }
        public bool DeviceForgeEnabled { get; set; }
        public bool Headers { get; set; }
        public bool Udp { get; set; }
        public bool Dns { get; set; }
        public bool Ntp { get; set; }
        public bool Mdns { get; set; }
        public bool Dhcp { get; set; }
        public bool Captive { get; set; }
        public bool AutoStartProxy { get; set; }
        public bool NetworkEnabled { get; set; }
        public int NetworkTTL { get; set; }
        public bool NetworkIPID { get; set; }
        public bool NetworkTCPOptions { get; set; }
        public bool NetworkForward { get; set; }
        public bool TunEnabled { get; set; }
        public string TunAdapter { get; set; }
        public string TunIP { get; set; }
        public string TunGatewayIP { get; set; }
        public int TunPrefixLen { get; set; }
        public bool TunRoute { get; set; }
        public string TunUpstreamInterface { get; set; }
        public string Mode { get; set; }
        public string Profile { get; set; }
    }

    public static class ProxyConfigManager
    {
        public static string DefaultConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"); }
        }

        public static ProxySettings Load(string path = null)
        {
            string configPath = string.IsNullOrEmpty(path) ? DefaultConfigPath : path;
            if (!File.Exists(configPath)) File.WriteAllText(configPath, DefaultJson(), new UTF8Encoding(false));

            var serializer = new JavaScriptSerializer();
            object parsed = serializer.DeserializeObject(File.ReadAllText(configPath, Encoding.UTF8));
            IDictionary<string, object> root = parsed as IDictionary<string, object> ?? new Dictionary<string, object>();
            IDictionary<string, object> deviceforge = GetDict(root, "deviceforge");
            IDictionary<string, object> udp = GetDict(deviceforge, "udp");
            IDictionary<string, object> dns = GetDict(deviceforge, "dns");
            IDictionary<string, object> ntp = GetDict(deviceforge, "ntp");
            IDictionary<string, object> mdns = GetDict(deviceforge, "mdns");
            IDictionary<string, object> dhcp = GetDict(deviceforge, "dhcp");
            IDictionary<string, object> captive = GetDict(deviceforge, "captive");
            IDictionary<string, object> network = GetDict(root, "network");
            IDictionary<string, object> tun = GetDict(root, "tun");

            return new ProxySettings
            {
                Enabled = GetBool(root, "enabled", true),
                DeviceForgeEnabled = GetBool(deviceforge, "enabled", true),
                Headers = GetBool(deviceforge, "headers", true),
                Udp = GetBool(udp, "enabled", false),
                Dns = GetBool(dns, "enabled", false),
                Ntp = GetBool(ntp, "enabled", false),
                Mdns = GetBool(mdns, "enabled", false),
                Dhcp = GetBool(dhcp, "enabled", false),
                Captive = GetBool(captive, "enabled", true),
                AutoStartProxy = GetBool(root, "gui_auto_start_proxy", true),
                NetworkEnabled = GetBool(network, "enabled", false),
                NetworkTTL = GetInt(network, "ttl", 64),
                NetworkIPID = GetBool(network, "ipid", true),
                NetworkTCPOptions = GetBool(network, "tcp_options", true),
                NetworkForward = GetBool(network, "forward", true),
                TunEnabled = GetBool(tun, "enabled", false),
                TunAdapter = GetString(tun, "name", "OneNetShare Tunnel"),
                TunIP = GetString(tun, "ip", "10.66.0.1"),
                TunGatewayIP = GetString(tun, "gateway_ip", "10.66.0.2"),
                TunPrefixLen = GetInt(tun, "prefix_len", 24),
                TunRoute = GetBool(tun, "route", true),
                TunUpstreamInterface = GetString(tun, "upstream_interface", ""),
                Mode = GetString(root, "mode", "forward"),
                Profile = GetString(root, "default_profile", "chrome-win11")
            };
        }

        public static List<string> GetProfiles(string path = null)
        {
            string configPath = string.IsNullOrEmpty(path) ? DefaultConfigPath : path;
            var profiles = new List<string> { "chrome-win11", "chrome-win10", "firefox-win11", "safari-macos", "mobile-android", "mobile-ios" };
            if (!File.Exists(configPath)) return profiles;

            try
            {
                var serializer = new JavaScriptSerializer();
                object parsed = serializer.DeserializeObject(File.ReadAllText(configPath, Encoding.UTF8));
                IDictionary<string, object> root = parsed as IDictionary<string, object>;
                if (root == null || !root.ContainsKey("profiles")) return profiles;
                IDictionary<string, object> custom = root["profiles"] as IDictionary<string, object>;
                if (custom == null) return profiles;
                foreach (string key in custom.Keys)
                    if (!profiles.Contains(key)) profiles.Add(key);
            }
            catch { }
            return profiles;
        }

        public static void Save(ProxySettings settings, string path = null)
        {
            string configPath = string.IsNullOrEmpty(path) ? DefaultConfigPath : path;
            if (!File.Exists(configPath)) File.WriteAllText(configPath, DefaultJson(), new UTF8Encoding(false));

            var serializer = new JavaScriptSerializer();
            object parsed = serializer.DeserializeObject(File.ReadAllText(configPath, Encoding.UTF8));
            IDictionary<string, object> root = parsed as IDictionary<string, object> ?? new Dictionary<string, object>();
            IDictionary<string, object> deviceforge = GetOrCreateDict(root, "deviceforge");

            SetBool(root, "enabled", settings.Enabled);
            SetString(root, "mode", string.IsNullOrEmpty(settings.Mode) ? "forward" : settings.Mode);
            SetString(root, "default_profile", string.IsNullOrEmpty(settings.Profile) ? "chrome-win11" : settings.Profile);
            SetBool(root, "gui_auto_start_proxy", settings.AutoStartProxy);

            IDictionary<string, object> network = GetOrCreateDict(root, "network");
            SetBool(network, "enabled", settings.NetworkEnabled);
            SetInt(network, "ttl", settings.NetworkTTL);
            SetBool(network, "ipid", settings.NetworkIPID);
            SetBool(network, "tcp_options", settings.NetworkTCPOptions);
            SetBool(network, "forward", settings.NetworkForward);

            IDictionary<string, object> tun = GetOrCreateDict(root, "tun");
            SetBool(tun, "enabled", settings.TunEnabled);
            SetString(tun, "name", string.IsNullOrEmpty(settings.TunAdapter) ? "OneNetShare Tunnel" : settings.TunAdapter);
            SetString(tun, "ip", string.IsNullOrEmpty(settings.TunIP) ? "10.66.0.1" : settings.TunIP);
            SetString(tun, "gateway_ip", string.IsNullOrEmpty(settings.TunGatewayIP) ? "10.66.0.2" : settings.TunGatewayIP);
            SetInt(tun, "prefix_len", settings.TunPrefixLen <= 0 ? 24 : settings.TunPrefixLen);
            SetBool(tun, "route", settings.TunRoute);
            SetString(tun, "upstream_interface", settings.TunUpstreamInterface ?? string.Empty);

            SetBool(deviceforge, "enabled", settings.DeviceForgeEnabled);
            SetBool(deviceforge, "headers", settings.Headers);
            SetBool(GetOrCreateDict(deviceforge, "udp"), "enabled", settings.Udp);
            SetBool(GetOrCreateDict(deviceforge, "dns"), "enabled", settings.Dns);
            SetBool(GetOrCreateDict(deviceforge, "ntp"), "enabled", settings.Ntp);
            SetBool(GetOrCreateDict(deviceforge, "mdns"), "enabled", settings.Mdns);
            SetBool(GetOrCreateDict(deviceforge, "dhcp"), "enabled", settings.Dhcp);
            SetBool(GetOrCreateDict(deviceforge, "captive"), "enabled", settings.Captive);

            File.WriteAllText(configPath, serializer.Serialize(root), new UTF8Encoding(false));
        }

        private static IDictionary<string, object> GetDict(IDictionary<string, object> parent, string key)
        {
            if (parent == null || !parent.ContainsKey(key)) return new Dictionary<string, object>();
            return parent[key] as IDictionary<string, object> ?? new Dictionary<string, object>();
        }

        private static IDictionary<string, object> GetOrCreateDict(IDictionary<string, object> parent, string key)
        {
            IDictionary<string, object> existing = GetDict(parent, key);
            if (parent.ContainsKey(key) && parent[key] is IDictionary<string, object>) return existing;
            parent[key] = existing;
            return existing;
        }

        private static bool GetBool(IDictionary<string, object> dict, string key, bool fallback)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value)) return fallback;
            if (value is bool) return (bool)value;
            bool parsed;
            return bool.TryParse(Convert.ToString(value), out parsed) ? parsed : fallback;
        }

        private static int GetInt(IDictionary<string, object> dict, string key, int fallback)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value)) return fallback;
            int parsed;
            if (value is int) return (int)value;
            return int.TryParse(Convert.ToString(value), out parsed) ? parsed : fallback;
        }

        private static string GetString(IDictionary<string, object> dict, string key, string fallback)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value)) return fallback;
            string text = Convert.ToString(value);
            return string.IsNullOrEmpty(text) ? fallback : text;
        }

        private static void SetBool(IDictionary<string, object> dict, string key, bool value)
        {
            dict[key] = value;
        }

        private static void SetInt(IDictionary<string, object> dict, string key, int value)
        {
            dict[key] = value;
        }

        private static void SetString(IDictionary<string, object> dict, string key, string value)
        {
            dict[key] = value;
        }

        private static string DefaultJson()
        {
            return @"{
  ""enabled"": true,
  ""mode"": ""forward"",
  ""listen_http"": ""0.0.0.0:18080"",
  ""listen_https"": ""127.0.0.1:18443"",
  ""default_profile"": ""chrome-win11"",
  ""auto_reload"": true,
  ""gui_auto_start_proxy"": true,
  ""network"": { ""enabled"": false, ""ttl"": 64, ""ipid"": true, ""tcp_options"": true, ""forward"": true },
  ""tun"": { ""enabled"": false, ""name"": ""OneNetShare Tunnel"", ""ip"": ""10.66.0.1"", ""gateway_ip"": ""10.66.0.2"", ""prefix_len"": 24, ""route"": true, ""upstream_interface"": """" },
  ""log"": { ""level"": ""info"", ""file"": "" },
  ""deviceforge"": {
    ""enabled"": true,
    ""target_os"": ""windows-11"",
    ""headers"": true,
    ""udp"": { ""enabled"": false },
    ""dns"": { ""enabled"": false },
    ""ntp"": { ""enabled"": false },
    ""mdns"": { ""enabled"": false },
    ""dhcp"": { ""enabled"": false },
    ""captive"": { ""enabled"": true, ""mode"": ""reply"" }
  },
  ""profiles"": {},
  ""rules"": []
}";
        }
    }
}
