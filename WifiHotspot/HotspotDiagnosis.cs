using System.Collections.Generic;

namespace WifiHotspot
{
    public class HotspotIssue
    {
        public string Title { get; set; }
        public string Detail { get; set; }
        public string FixPath { get; set; }
        public string SettingsUri { get; set; }
    }

    public class HotspotDiagnosis
    {
        public HotspotDiagnosis()
        {
            Issues = new List<HotspotIssue>();
        }

        public string RawMessage { get; set; }
        public List<HotspotIssue> Issues { get; set; }
    }
}
