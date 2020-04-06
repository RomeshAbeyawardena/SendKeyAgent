using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SendKeyAgent.App
{
    public struct Version
    {
        public Version(uint major, uint minor, uint revision, uint patch)
            : this(major, minor, revision, patch, 
                   default, default, default, default)
        {
        }

        public Version(uint major, uint minor, uint revision, uint patch, 
            uint majorMaximum, uint minorMaximum, uint revisionMaximum, uint patchMaximum)
        {
            Major = major;
            Minor = minor;
            Revision = revision;
            Patch = patch;
            MajorMaximum = majorMaximum;
            MinorMaximum = minorMaximum;
            RevisionMaximum = revisionMaximum;
            PatchMaximum = patchMaximum;
        }

        internal void IncrementPatch()
        {
            if(Patch + 1 > PatchMaximum)
            {
                IncrementRevision();
                Patch = 0;
            }
            else
            {
                Patch++;
            }
        }

        internal void IncrementRevision()
        {
            if(Revision + 1 > RevisionMaximum)
            {
                IncrementMinor();
                Revision = 0;
            }
            else
            {
                Revision++;
            }
        }

        internal void IncrementMinor()
        {
            if(Minor + 1 > MinorMaximum)
            {
                IncrementMajor();
                Minor = 0;
            }
            else
            {
                Minor++;
            }
        }

        internal void IncrementMajor()
        {
            Major++;
        }

        public static Version Parse(string versionString)
        {
            if(string.IsNullOrWhiteSpace(versionString))
                throw new ArgumentNullException(nameof(versionString));

            var versionParts = versionString.Split(".", 
                StringSplitOptions.RemoveEmptyEntries);

            if(versionParts.Length == 0)
                throw new ArgumentException("No version parts available");

            var version = new Version();

            if(versionParts.Length == 4 && uint.TryParse(versionParts[3], out var patch))
            {
                version.Patch = patch;
            }

            if(versionParts.Length >= 3 && uint.TryParse(versionParts[2], out var revision))
            {
                version.Revision = revision;
            }

            if(versionParts.Length >= 2 && uint.TryParse(versionParts[1], out var minor))
            {
                version.Minor = minor;
            }

            if(versionParts.Length >= 1 && uint.TryParse(versionParts[0], out var major))
            {
                version.Major = major;
            }

            return version;
        }

        public static Version operator ++(Version a)
        {
            a.IncrementPatch();
            return a;
        }

        public static Version operator +(Version a, Version b)
        {
            return new Version (
                a.Major + b.Major, 
                a.Minor + b.Minor, 
                a.Revision + b.Revision, 
                a.Patch + b.Patch);
        }

        public static Version operator -(Version a, Version b)
        {
            return new Version (
                a.Major - b.Major, 
                a.Minor - b.Minor, 
                a.Revision - b.Revision, 
                a.Patch - b.Patch);
        }

        public static bool operator ==(Version a, Version b)
        {
            return a.Major == b.Major
                && a.Minor == b.Minor
                && a.Revision == b.Revision
                && a.Patch == b.Patch;
        }

        public static bool operator !=(Version a, Version b)
        {
            return a.Major != b.Major
                && a.Minor != b.Minor
                && a.Revision != b.Revision
                && a.Patch != b.Patch;
        }

        public static bool operator %(Version version, uint a)
        {
            return version.Major % a == 1
                || version.Minor % a == 1
                || version.Revision % a == 1
                || version.Patch % a == 1;
        }

        public override bool Equals(object obj)
        {
            if(!(obj is Version versionObj))
                return false;

            return versionObj == this;
        }

        public override int GetHashCode()
        {
            return HashCode
                .Combine(Major, Minor, Revision, Patch);
        }

        public uint Major { get; private set; }

        public uint Minor { get; private set; }

        public uint Revision { get; private set; }

        public uint Patch { get; private set; }

        internal uint MajorMaximum { get; }

        internal uint MinorMaximum { get; }

        internal uint RevisionMaximum { get; }

        internal uint PatchMaximum { get; }
    }
}
