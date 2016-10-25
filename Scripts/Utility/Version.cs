using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Rynchodon
{
	public struct Version : IComparable<Version>
	{
		public readonly int Major, Minor, Build, Revision;

		public Version(int major, int minor, int build, int revision = 0)
		{
			this.Major = major;
			this.Minor = minor;
			this.Build = build;
			this.Revision = revision;
		}

		public Version(int revision)
		{
			this.Major = this.Minor = this.Build = 0;
			this.Revision = revision;
		}

		public Version(string version)
		{
			Regex versionParts = new Regex(@"(\d+)\.(\d+)\.(\d+)\.?(\d*)");
			Match match = versionParts.Match(version);

			if (!match.Success)
			{
				// backward compatibility, old versions are considered to be 1.X.0.0
				if (int.TryParse(version, out this.Minor))
				{
					this.Major = 1;
					this.Build = this.Revision = 0;
				}
				else
					throw new ArgumentException("Could not parse: " + version);
				return;
			}

			string group = match.Groups[1].Value;
			this.Major = string.IsNullOrWhiteSpace(group) ? 0 : int.Parse(group);
			group = match.Groups[2].Value;
			this.Minor = string.IsNullOrWhiteSpace(group) ? 0 : int.Parse(group);
			group = match.Groups[3].Value;
			this.Build = string.IsNullOrWhiteSpace(group) ? 0 : int.Parse(group);
			group = match.Groups[4].Value;
			this.Revision = string.IsNullOrWhiteSpace(group) ? 0 : int.Parse(group);
		}

		public Version(FileVersionInfo version)
		{
			Major = Math.Min(version.FileMajorPart, version.ProductMajorPart);
			Minor = Math.Min(version.FileMinorPart, version.ProductMinorPart);
			Build = Math.Min(version.FileBuildPart, version.ProductBuildPart);
			Revision = Math.Min(version.FilePrivatePart, version.ProductPrivatePart);
		}

		public int CompareTo(Version other)
		{
			int diff = this.Major - other.Major;
			if (diff != 0)
				return diff;
			diff = this.Minor - other.Minor;
			if (diff != 0)
				return diff; 
			diff = this.Build - other.Build;
			if (diff != 0)
				return diff; 
			diff = this.Revision - other.Revision;
			if (diff != 0)
				return diff;

			return 0;
		}

		public override string ToString()
		{
			return Major + "." + Minor + "." + Build + "." + Revision;
		}
	}
}
