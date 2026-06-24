using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PcCam_x64.Infrastructure
{
    /// <summary>
    /// INI 파일 읽기/쓰기 Helper.
    /// 
    /// 기존 kernel32 WritePrivateProfileString 방식 대신
    /// C# 파일 직접 읽기/쓰기 방식으로 처리한다.
    /// 
    /// 저장 인코딩:
    /// - UTF-8 with BOM
    /// 
    /// 목적:
    /// - DisplayName 같은 한글 설정값이 깨지지 않도록 처리
    /// - 설정 파일을 메모장/편집기에서 열었을 때 한글이 정상 표시되도록 처리
    /// </summary>
    public class IniFileHelper
    {
        private readonly string _filePath;

        /// <summary>
        /// UTF-8 BOM 포함 인코딩.
        /// 
        /// Windows 메모장 및 일부 구형 편집기 호환성을 위해 BOM을 포함한다.
        /// </summary>
        private static readonly Encoding Utf8WithBom = new UTF8Encoding(true);

        /// <summary>
        /// INI 파일 Helper를 생성한다.
        /// </summary>
        /// <param name="filePath">
        /// INI 파일 경로.
        /// </param>
        public IniFileHelper(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("INI 파일 경로가 비어 있습니다.", "filePath");

            _filePath = filePath;

            EnsureFile();
        }

        /// <summary>
        /// 문자열 값을 읽는다.
        /// </summary>
        /// <param name="section">
        /// INI 섹션명.
        /// </param>
        /// <param name="key">
        /// INI 키명.
        /// </param>
        /// <param name="defaultValue">
        /// 값이 없을 때 반환할 기본값.
        /// </param>
        /// <returns>
        /// INI에 저장된 문자열 값.
        /// </returns>
        public string ReadString(string section, string key, string defaultValue)
        {
            if (string.IsNullOrWhiteSpace(section))
                return defaultValue ?? "";

            if (string.IsNullOrWhiteSpace(key))
                return defaultValue ?? "";

            List<string> lines = ReadAllLines();

            string currentSection = "";

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];

                string parsedSection;

                if (TryParseSection(line, out parsedSection))
                {
                    currentSection = parsedSection;
                    continue;
                }

                if (!string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                    continue;

                string parsedKey;
                string parsedValue;

                if (!TryParseKeyValue(line, out parsedKey, out parsedValue))
                    continue;

                if (string.Equals(parsedKey, key, StringComparison.OrdinalIgnoreCase))
                    return parsedValue;
            }

            return defaultValue ?? "";
        }

        /// <summary>
        /// 문자열 값을 저장한다.
        /// 
        /// 저장 시 파일 전체를 UTF-8 with BOM으로 다시 기록한다.
        /// </summary>
        /// <param name="section">
        /// INI 섹션명.
        /// </param>
        /// <param name="key">
        /// INI 키명.
        /// </param>
        /// <param name="value">
        /// 저장할 값.
        /// </param>
        public void WriteString(string section, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(section))
                throw new ArgumentException("INI 섹션명이 비어 있습니다.", "section");

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("INI 키명이 비어 있습니다.", "key");

            if (value == null)
                value = "";

            /*
             * INI 한 줄 값을 깨지지 않게 저장하기 위해 줄바꿈은 공백으로 치환한다.
             */
            value = value
                .Replace("\r", " ")
                .Replace("\n", " ");

            List<string> lines = ReadAllLines();

            int sectionStartIndex = -1;
            int sectionEndIndex = lines.Count;

            string currentSection = "";

            for (int i = 0; i < lines.Count; i++)
            {
                string parsedSection;

                if (!TryParseSection(lines[i], out parsedSection))
                    continue;

                if (sectionStartIndex >= 0)
                {
                    sectionEndIndex = i;
                    break;
                }

                currentSection = parsedSection;

                if (string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                {
                    sectionStartIndex = i;
                    sectionEndIndex = lines.Count;
                }
            }

            /*
             * 섹션이 없으면 맨 아래에 섹션과 키를 추가한다.
             */
            if (sectionStartIndex < 0)
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                    lines.Add("");

                lines.Add("[" + section + "]");
                lines.Add(key + "=" + value);

                WriteAllLines(lines);
                return;
            }

            /*
             * 섹션 내부에서 기존 키를 찾는다.
             */
            for (int i = sectionStartIndex + 1; i < sectionEndIndex; i++)
            {
                string parsedKey;
                string parsedValue;

                if (!TryParseKeyValue(lines[i], out parsedKey, out parsedValue))
                    continue;

                if (string.Equals(parsedKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = key + "=" + value;
                    WriteAllLines(lines);
                    return;
                }
            }

            /*
             * 키가 없으면 섹션 끝에 추가한다.
             */
            lines.Insert(sectionEndIndex, key + "=" + value);

            WriteAllLines(lines);
        }

        /// <summary>
        /// 정수 값을 읽는다.
        /// </summary>
        public int ReadInt(string section, string key, int defaultValue)
        {
            string value = ReadString(section, key, defaultValue.ToString());

            int result;

            if (int.TryParse(value, out result))
                return result;

            return defaultValue;
        }

        /// <summary>
        /// 정수 값을 저장한다.
        /// </summary>
        public void WriteInt(string section, string key, int value)
        {
            WriteString(section, key, value.ToString());
        }

        /// <summary>
        /// bool 값을 읽는다.
        /// </summary>
        public bool ReadBool(string section, string key, bool defaultValue)
        {
            string value = ReadString(section, key, defaultValue ? "True" : "False");

            bool result;

            if (bool.TryParse(value, out result))
                return result;

            return defaultValue;
        }

        /// <summary>
        /// bool 값을 저장한다.
        /// </summary>
        public void WriteBool(string section, string key, bool value)
        {
            WriteString(section, key, value ? "True" : "False");
        }

        /// <summary>
        /// 파일이 없으면 UTF-8 BOM 형식으로 생성한다.
        /// </summary>
        private void EnsureFile()
        {
            string directory = Path.GetDirectoryName(_filePath);

            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            if (!File.Exists(_filePath))
            {
                File.WriteAllText(_filePath, "", Utf8WithBom);
            }
        }

        /// <summary>
        /// INI 파일의 모든 줄을 읽는다.
        /// 
        /// 기존 파일이 UTF-8 BOM이면 UTF-8로 읽고,
        /// UTF-16 LE BOM이면 Unicode로 읽고,
        /// BOM이 없으면 기존 ANSI 설정 파일 호환을 위해 시스템 기본 인코딩으로 읽는다.
        /// 
        /// 저장은 항상 UTF-8 BOM으로 수행한다.
        /// </summary>
        private List<string> ReadAllLines()
        {
            EnsureFile();

            Encoding encoding = DetectEncoding(_filePath);

            string[] lines = File.ReadAllLines(_filePath, encoding);

            return new List<string>(lines);
        }

        /// <summary>
        /// INI 파일의 모든 줄을 UTF-8 BOM으로 저장한다.
        /// </summary>
        private void WriteAllLines(List<string> lines)
        {
            if (lines == null)
                lines = new List<string>();

            string directory = Path.GetDirectoryName(_filePath);

            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllLines(_filePath, lines.ToArray(), Utf8WithBom);
        }

        /// <summary>
        /// 파일 BOM 기준으로 인코딩을 판단한다.
        /// 
        /// 새 저장은 항상 UTF-8 BOM으로 하므로,
        /// 기존 ANSI 파일도 한 번 저장 후 UTF-8 파일로 전환된다.
        /// </summary>
        private Encoding DetectEncoding(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);

            if (bytes.Length >= 3 &&
                bytes[0] == 0xEF &&
                bytes[1] == 0xBB &&
                bytes[2] == 0xBF)
            {
                return Utf8WithBom;
            }

            if (bytes.Length >= 2 &&
                bytes[0] == 0xFF &&
                bytes[1] == 0xFE)
            {
                return Encoding.Unicode;
            }

            if (bytes.Length >= 2 &&
                bytes[0] == 0xFE &&
                bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode;
            }

            /*
             * 기존 INI가 ANSI로 저장되어 있던 경우를 고려한다.
             * 한 번 저장하면 UTF-8 BOM으로 전환된다.
             */
            return Encoding.Default;
        }

        /// <summary>
        /// INI 섹션 라인인지 확인하고 섹션명을 추출한다.
        /// 
        /// 예:
        /// [Stream0]
        /// </summary>
        private bool TryParseSection(string line, out string section)
        {
            section = "";

            if (string.IsNullOrWhiteSpace(line))
                return false;

            string value = line.Trim();

            if (!value.StartsWith("[") || !value.EndsWith("]"))
                return false;

            if (value.Length <= 2)
                return false;

            section = value.Substring(1, value.Length - 2).Trim();

            return !string.IsNullOrWhiteSpace(section);
        }

        /// <summary>
        /// INI key=value 라인을 분석한다.
        /// 
        /// 주석 또는 빈 줄은 false를 반환한다.
        /// </summary>
        private bool TryParseKeyValue(
            string line,
            out string key,
            out string value)
        {
            key = "";
            value = "";

            if (string.IsNullOrWhiteSpace(line))
                return false;

            string trimmed = line.Trim();

            if (trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                return false;

            int equalIndex = line.IndexOf('=');

            if (equalIndex <= 0)
                return false;

            key = line.Substring(0, equalIndex).Trim();
            value = line.Substring(equalIndex + 1);

            return !string.IsNullOrWhiteSpace(key);
        }
    }
}