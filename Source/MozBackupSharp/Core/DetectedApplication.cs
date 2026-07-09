using System.Collections.Generic;

namespace MozBackupSharp.Core
{
    public sealed class DetectedApplication
    {
        public DetectedApplication()
        {
            Profiles = new List<MozillaProfile>();
        }

        public ApplicationKind Kind { get; set; }
        public string Name { get; set; }
        public string ProfilesIniPath { get; set; }
        public string RootDirectory { get; set; }
        public IList<MozillaProfile> Profiles { get; private set; }

        public override string ToString()
        {
            return Name;
        }
    }
}
