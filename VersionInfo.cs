namespace ScreenClip
{
    internal static class VersionInfo
    {
#if BUILD_COMMIT
        public const string CommitId = BuildCommit.Id;
#else
        public const string CommitId = "unversioned";
#endif

        public const string RepositoryUrl = "https://github.com/crono2250/ScreenClip";

        public static string DisplayVersion
        {
            get { return CommitId.Length == 40 ? CommitId.Substring(0, 12) : "未設定"; }
        }

        public static string CommitUrl
        {
            get { return RepositoryUrl + "/commit/" + CommitId; }
        }
    }
}
