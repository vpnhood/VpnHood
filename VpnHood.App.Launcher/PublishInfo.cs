namespace VpnHood.Common
{
    public class PublishInfo
    {
        /// <summary>
        /// Publish version
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Entry point dll of package after updating
        /// </summary>
        public string LaunchPath { get; set; }

        /// <summary>
        /// This argument will pass to LaunchPath
        /// </summary>
        public string[] LaunchArguments { get; set; }
        
        /// <summary>
        /// Url to Updated PublishInfo for next releases
        /// </summary>
        public string UpdateUrl { get; set; }
        
        /// <summary>
        /// Url to download this version
        /// </summary>
        public string PackageDownloadUrl { get; set; }

        /// <summary>
        /// PackageFileName
        /// </summary>
        public string PackageFileName { get; set; }
    }
}
