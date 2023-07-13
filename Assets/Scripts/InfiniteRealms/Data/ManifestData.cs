using System;
using System.Collections.Generic;

namespace InfiniteRealms.Data
{
    [Serializable]
    public class ManifestData
    {
        public string ScenarioName;
        public DateTime CreationTime;
        public string UnityVersion;
        public Dictionary<string, string> Files;
    }
}