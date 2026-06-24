using System;
using System.Reflection;

namespace PcCam_x64.Infrastructure
{
    /// <summary>
    /// 현재 실행 중인 PC CAM의 프로그램 버전을 제공한다.
    ///
    /// 버전 원본:
    /// Properties\AssemblyInfo.cs의 AssemblyFileVersion
    ///
    /// 화면이나 로그에서 버전 문자열을 직접 관리하지 않고
    /// 이 클래스를 통해 동일한 버전을 사용한다.
    /// </summary>
    public static class AppVersionProvider
    {
        private const string DefaultVersion = "0.0.0";

        /// <summary>
        /// 점으로 구분된 프로그램 버전을 반환한다.
        ///
        /// 예:
        /// AssemblyFileVersion = 3.0.3.0
        /// 반환값 = 3.0.3
        /// </summary>
        public static string Version
        {
            get
            {
                try
                {
                    Assembly assembly = Assembly.GetEntryAssembly();

                    if (assembly == null)
                        return DefaultVersion;

                    object[] attributes =
                        assembly.GetCustomAttributes(
                            typeof(AssemblyFileVersionAttribute),
                            false);

                    if (attributes == null || attributes.Length == 0)
                    {
                        return NormalizeVersion(
                            assembly.GetName().Version);
                    }

                    AssemblyFileVersionAttribute attribute =
                        attributes[0] as AssemblyFileVersionAttribute;

                    if (attribute == null ||
                        string.IsNullOrWhiteSpace(attribute.Version))
                    {
                        return NormalizeVersion(
                            assembly.GetName().Version);
                    }

                    Version parsedVersion;

                    if (!System.Version.TryParse(
                        attribute.Version,
                        out parsedVersion))
                    {
                        return attribute.Version;
                    }

                    return NormalizeVersion(parsedVersion);
                }
                catch
                {
                    /*
                     * 버전 조회 실패가 프로그램 실행을 막으면 안 되므로
                     * 기본값을 반환한다.
                     */
                    return DefaultVersion;
                }
            }
        }

        /// <summary>
        /// 화면 표시용 버전 문자열.
        ///
        /// 예:
        /// v3.0.3
        /// </summary>
        public static string DisplayVersion
        {
            get { return "v" + Version; }
        }

        /// <summary>
        /// Version 객체를 사용자 표시용 문자열로 변환한다.
        ///
        /// 마지막 Revision 값이 0이면 표시하지 않는다.
        /// </summary>
        private static string NormalizeVersion(Version version)
        {
            if (version == null)
                return DefaultVersion;

            if (version.Revision > 0)
            {
                return version.Major +
                       "." +
                       version.Minor +
                       "." +
                       version.Build +
                       "." +
                       version.Revision;
            }

            if (version.Build >= 0)
            {
                return version.Major +
                       "." +
                       version.Minor +
                       "." +
                       version.Build;
            }

            return version.Major +
                   "." +
                   version.Minor;
        }
    }
}