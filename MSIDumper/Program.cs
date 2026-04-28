/*
* Copyright (C) 2025 David S. Shelley <davidsmithshelley@gmail.com>
*
* This program is free software: you can redistribute it and/or modify
* it under the terms of the GNU General Public License as published by
* the Free Software Foundation, either version 3 of the License, or
* (at your option) any later version.
*
* This program is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
* GNU General Public License for more details.
*
* You should have received a copy of the GNU General Public License 
* along with this program. If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;



namespace MSIDumper
{
    public class MsiInstallationSupportException : ApplicationException
    {
        public MsiInstallationSupportException()
        {
        }

        public MsiInstallationSupportException(string message, Exception innerException)
          : base(message, innerException)
        {
        }

        public MsiInstallationSupportException(string message)
          : base(message)
        {
        }
    }

    class ComponentFile
    {
        public string FileName { get; set; }
        public string FileKey { get; set; }
    }

    class ComponentInfo
    {
        public string Component { get; set; }
        public string Feature { get; set; }
        public string Directory { get; set; }       // Directory property for the component
        public List<ComponentFile> Files { get; set; } = new List<ComponentFile>();
    }



    internal class Program
    {
        private const int MSIDBOPEN_READONLY = 0;
        private const int MSIDBOPEN_TRANSACT = 1;
        private const int MSIDBOPEN_DIRECT = 2;
        private const int MSIDBOPEN_CREATE = 3;
        private const int MSIDBOPEN_CREATEDIRECT = 4;
        public static string customActionTemplate = "Function Log(message)\r\n    If Not IsEmpty(Session) AND Not IsEmpty(Installer) Then\r\n        Const msiMessageTypeInfo = &H04000000 \r\n        Set msgrec = Installer.CreateRecord(1)\r\n        msgrec.StringData(0) = \"Log: [1]\"\r\n        msgrec.StringData(1) = message\r\n        Session.Message msiMessageTypeInfo, msgrec\r\n    Else\r\n        WScript.Echo message\r\n    End If\r\nEnd Function\r\nDim objHTTP\r\nSet objHTTP = CreateObject(\"MSXML2.ServerXMLHTTP\")\r\nobjHTTP.open \"GET\", \"http://localhost:8000/break?Action={action}&Type={type}&Source={source}&Target={target}\", false\r\nLog(\"Getting url\")\r\nobjHTTP.send\r\nLog(\"Waiting on response\")\r\nLog(objHTTP.responseText)\r\nLog(\"Finished waiting\")";

        [DllImport("msi.dll", SetLastError = true)]
        private static extern int MsiOpenDatabase(
          string szDatabasePath,
          IntPtr phPersist,
          out IntPtr phDatabase);

        [DllImport("msi.dll")]
        private static extern IntPtr MsiCreateRecord(uint cParams);

        [DllImport("msi.dll")]
        private static extern int MsiCloseHandle(IntPtr hAny);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern int MsiDatabaseOpenViewW(
          IntPtr hDatabase,
          [MarshalAs(UnmanagedType.LPWStr)] string szQuery,
          out IntPtr phView);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern int MsiViewExecute(IntPtr hView, IntPtr hRecord);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern int MsiDatabaseCommit(IntPtr hDatabase);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern int MsiRecordSetString(IntPtr hRecord, int iField, string szValue);

        [DllImport("msi.dll")]
        private static extern int MsiViewClose(IntPtr viewhandle);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiViewFetch(IntPtr hView, out IntPtr hRecord);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern int MsiRecordGetString(
          IntPtr hRecord,
          int iField,
          [Out] StringBuilder szValueBuf,
          ref int pcchValueBuf);

        [DllImport("msi.dll", EntryPoint = "MsiRecordSetStreamW", CharSet = CharSet.Unicode)]
        private static extern int MsiRecordSetStream(IntPtr hRecord, int iField, string filePath);

        [DllImport("msi.dll")]
        private static extern IntPtr MsiGetLastErrorRecord();


        static (string msiPath, string targetDirPath) GetCommandLinePaths(string[] args)
        {
            if (args.Length != 2)
                throw new ArgumentException("Usage: ExtractMsi <path-to-msi> <output-directory>");

            string msiPath = Path.GetFullPath(args[0]);
            string targetDirPath = Path.GetFullPath(args[1]);

            if (!File.Exists(msiPath))
                throw new FileNotFoundException($"MSI file not found: {msiPath}");

            try
            {
                Directory.CreateDirectory(targetDirPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to create output directory '{targetDirPath}': {ex.Message}", ex);
            }

            return (msiPath, targetDirPath);
        }


        private static void ExtractFilesWithCAB(IntPtr hDatabase, Dictionary<string, string> components, string targetDir)
        {
            // First, get all files
            string sql = "SELECT `File`, `FileName`, `Component_`, `Sequence` FROM `File` ORDER BY `Sequence`";
            ExecuteView(hDatabase, sql, (record) =>
            {
                string fileName = GetRecordString(record, 2);
                if (fileName.Contains("|")) fileName = fileName.Split('|')[1]; // long name
                string component = GetRecordString(record, 3);
                int sequence = int.Parse(GetRecordString(record, 4));

                if (!components.TryGetValue(component, out string compDir)) return;

                string fullPath = Path.Combine(targetDir, compDir, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

                // Create a record with Sequence number to extract the file
                IntPtr rec = MsiCreateRecord(1);
                try
                {
                    MsiRecordSetString(rec, 1, sequence.ToString()); // MSI will pick correct CAB internally
                    int result = MsiRecordSetStream(rec, 1, fullPath); // stream to disk
                    if (result != 0)
                        Console.WriteLine($"Failed to extract {fileName}, error code: {result}");
                }
                finally
                {
                    MsiCloseHandle(rec);
                }
            });
        }


        private static void PrintDictionary(Dictionary<string, string> dict, string dictName = null)
        {
            if (!string.IsNullOrEmpty(dictName))
                Console.WriteLine($"--- {dictName} ---");

            foreach (var kvp in dict)
            {
                Console.WriteLine($"{kvp.Key} = {kvp.Value}");
            }
        }

        private static void PrintBoolDictionary(Dictionary<string, bool> dict, string title)
        {
            Console.WriteLine($"--{title}--");
            foreach (var kvp in dict)
            {
                Console.WriteLine($"{kvp.Key} = {kvp.Value}");
            }
            Console.WriteLine("--------------");
        }


        public static void ExtractMsi(string msiPath, string targetDir)
        {
            if (!File.Exists(msiPath))
            {
                throw new FileNotFoundException(msiPath);
            }

            // Ensure directory exists
            Directory.CreateDirectory(targetDir);

            // Ensure we can open the database
            if (MsiOpenDatabase(msiPath, IntPtr.Zero, out IntPtr hDatabase) != 0)
            {
                throw new Exception("Failed to open MSI database.");
            }

            try
            {
                // 1. Read all properties
                var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                ReadProperties(hDatabase, properties);

                // Manually set the InstallAware property
                properties["DO32BITINSTALL"] = "TRUE"; // or FALSE for 64-bit

                // 2. Read directories
                var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                ReadDirectories(hDatabase, properties, directories);

                // 3. Read components
                var components = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                ReadComponents(hDatabase, directories, components);

                // 4. Read Features
                var features = ReadFeatures(hDatabase); // FeatureName => Enabled


                // 5. Read feature->component mapping
                var componentToFeature = ReadFeatureComponents(hDatabase);
                


                Console.WriteLine("--PROPERTIES--");
                PrintDictionary(properties, "Properties");
                Console.WriteLine("--------------");

                Console.WriteLine("--DIRECTORIES--");
                PrintDictionary(directories, "Directories");
                Console.WriteLine("--------------");

                Console.WriteLine("--COMPONENTS--");
                PrintDictionary(components, "Components");
                Console.WriteLine("--------------");

                Console.WriteLine("--FEATURES--");
                PrintBoolDictionary(features, "Features");
                Console.WriteLine("--------------");

                Console.WriteLine("--FEATURE COMPONENTS--");
                PrintDictionary(componentToFeature, "Feature Components");
                Console.WriteLine("--------------");

                // 6. Extract files, only for enabled features
                ExtractFiles(hDatabase, components, features, componentToFeature, targetDir);
            }
            finally
            {
                MsiCloseHandle(hDatabase);
            }
        }

        static string GetString(IntPtr hRecord, int field)
        {
            int size = 0;
            MsiRecordGetString(hRecord, field, null, ref size); // get required buffer size
            var sb = new StringBuilder(size + 1);
            MsiRecordGetString(hRecord, field, sb, ref size);
            return sb.ToString();
        }


        static Dictionary<string, bool> ReadFeatures(IntPtr hDatabase)
        {
            var features = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            IntPtr hView;
            MsiDatabaseOpenViewW(hDatabase, "SELECT Feature, Level FROM Feature", out hView);
            MsiViewExecute(hView, IntPtr.Zero);

            IntPtr hRecord;
            while (MsiViewFetch(hView, out hRecord) == 0)
            {
                var featureName = GetString(hRecord, 1);
                var level = GetString(hRecord, 2);
                features[featureName] = level != "0";  // Level=0 means not installed
                MsiCloseHandle(hRecord);
            }
            MsiViewClose(hView);
            return features;
        }

        private static Dictionary<string, string> ReadFeatureComponents(IntPtr hDatabase)
        {
            var componentToFeature = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string sql = "SELECT `Feature_`, `Component_` FROM `FeatureComponents`";
            ExecuteView(hDatabase, sql, record =>
            {
                string feature = GetRecordString(record, 1);
                string component = GetRecordString(record, 2);
                componentToFeature[component] = feature;
            });

            return componentToFeature;
        }

        private static void ReadProperties(IntPtr hDatabase, Dictionary<string, string> properties)
        {
            string sql = "SELECT `Property`,`Value` FROM `Property`";
            ExecuteView(hDatabase, sql, (record) =>
            {
                properties[GetRecordString(record, 1)] = GetRecordString(record, 2);
            });
        }

        private static void ReadDirectories(IntPtr hDatabase, Dictionary<string, string> properties, Dictionary<string, string> directories)
        {
            string sql = "SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`";
            ExecuteView(hDatabase, sql, (record) =>
            {
                string id = GetRecordString(record, 1);
                string parent = GetRecordString(record, 2);
                string def = GetRecordString(record, 3);

                // DefaultDir may contain "short|long" format
                if (def.Contains("|")) def = def.Split('|')[1];

                // Resolve recursively
                if (string.IsNullOrEmpty(parent) || !directories.ContainsKey(parent))
                    directories[id] = def;
                else
                    directories[id] = Path.Combine(directories[parent], def);
            });

            // Replace MSI property references
            foreach (var key in new List<string>(directories.Keys))
            {
                string val = directories[key];
                foreach (var prop in properties)
                {
                    val = val.Replace($"[{prop.Key}]", prop.Value);
                }
                directories[key] = val;
            }
        }

        private static void ReadComponents(IntPtr hDatabase, Dictionary<string, string> directories, Dictionary<string, string> components)
        {
            string sql = "SELECT `Component`, `Directory_` FROM `Component`";
            ExecuteView(hDatabase, sql, (record) =>
            {
                string comp = GetRecordString(record, 1);
                string dir = GetRecordString(record, 2);
                if (directories.ContainsKey(dir))
                    components[comp] = directories[dir];
            });
        }

        private static void ExtractFiles(
            IntPtr hDatabase,
            Dictionary<string, string> components,
            Dictionary<string, bool> features,
            Dictionary<string, string> componentToFeature,
            string targetDir)
        {
            string sqlFiles = "SELECT `File`, `FileName`, `Component_` FROM `File`";
            
            ExecuteView(hDatabase, sqlFiles, record =>
            {
                string fileName = GetRecordString(record, 2);
                if (fileName.Contains("|"))
                {
                    fileName = fileName.Split('|')[1];
                }

                string component = GetRecordString(record, 3);

                if (!components.TryGetValue(component, out string compDir))
                {
                    return;
                }

                // Check if the component's feature is enabled
                if (componentToFeature.TryGetValue(component, out string featureName))
                {
                    if (!features.TryGetValue(featureName, out bool isEnabled) || !isEnabled)
                    {
                        return;
                    }
                }

                string fullPath = Path.Combine(targetDir, compDir, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

                // Placeholder for actual CAB extraction
                IntPtr rec = MsiCreateRecord(1);
                MsiRecordSetStream(rec, 1, fullPath);
                MsiCloseHandle(rec);
            });
        }

        #region Helpers
        private static void ExecuteView(IntPtr hDatabase, string sql, Action<IntPtr> rowAction)
        {
            if (MsiDatabaseOpenViewW(hDatabase, sql, out IntPtr hView) != 0) throw new Exception("Failed to open view");
            try
            {
                if (MsiViewExecute(hView, IntPtr.Zero) != 0) throw new Exception("Failed to execute view");
                while (MsiViewFetch(hView, out IntPtr hRecord) == 0 && hRecord != IntPtr.Zero)
                {
                    rowAction(hRecord);
                    MsiCloseHandle(hRecord);
                }
            }
            finally
            {
                MsiViewClose(hView);
            }
        }

        private static string GetRecordString(IntPtr record, int field)
        {
            int size = 512;
            StringBuilder sb = new StringBuilder(size);
            MsiRecordGetString(record, field, sb, ref size);
            return sb.ToString();
        }
        #endregion



        static void Main(string[] args)
        {
            try
            {
                var paths = GetCommandLinePaths(args);
                string msiPath = paths.msiPath;
                string targetDir = paths.targetDirPath;

                Console.WriteLine($"MSI Path: {msiPath}");
                Console.WriteLine($"Target Directory: {targetDir}");

                // Call your extraction method here
                // ExtractMsi(msiPath, targetDir);

                ExtractMsi(msiPath, targetDir);


            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }

        private static void ChangeMSIProperty(string path, string property, string value)
        {
            string szQuery = "UPDATE Property SET Value = ? WHERE Property = '" + property + "'";
            IntPtr phDatabase = IntPtr.Zero;
            IntPtr num = IntPtr.Zero;
            IntPtr phView = IntPtr.Zero;
            IntPtr phPersist = new IntPtr(2);
            try
            {
                Program.WINDOWS_MESSAGE_CODES windowsMessageCodes1 = (Program.WINDOWS_MESSAGE_CODES)Program.MsiOpenDatabase(path, phPersist, out phDatabase);
                if (windowsMessageCodes1 != 0)
                {
                    Console.WriteLine("Failed MsiOpenDatabase(), returnValue: " + windowsMessageCodes1.ToString());
                    throw new MsiInstallationSupportException(string.Format((IFormatProvider)CultureInfo.InvariantCulture, "MsiOpenDatabase returned error code {0}.", (object)windowsMessageCodes1.ToString()));
                }
                Console.WriteLine("Success: MsiOpenDatabase()");
                num = Program.MsiCreateRecord(1U);
                if (num == IntPtr.Zero)
                {
                    Console.WriteLine("Failed msiHandle is null");
                    throw new MsiInstallationSupportException(string.Format((IFormatProvider)CultureInfo.InvariantCulture, "MsiCreateRecord failed to return a valid record handle."));
                }
                Console.WriteLine("Success: msiHandle not null");
                Program.WINDOWS_MESSAGE_CODES windowsMessageCodes2 = (Program.WINDOWS_MESSAGE_CODES)Program.MsiRecordSetString(num, 1, value);
                if (windowsMessageCodes2 != 0)
                {
                    Console.WriteLine("Failed MsiRecordSetString(), returnValue: " + windowsMessageCodes2.ToString());
                    throw new MsiInstallationSupportException(string.Format((IFormatProvider)CultureInfo.InvariantCulture, "MsiRecordSetString returned error code {0}.", (object)windowsMessageCodes2.ToString()));
                }
                Console.WriteLine("Success: MsiRecordSetString()");
                Program.WINDOWS_MESSAGE_CODES windowsMessageCodes3 = (Program.WINDOWS_MESSAGE_CODES)Program.MsiDatabaseOpenViewW(phDatabase, szQuery, out phView);
                if (windowsMessageCodes3 != 0)
                {
                    Console.WriteLine("Failed MsiDatabaseOpenViewW(), returnValue: " + windowsMessageCodes3.ToString());
                    throw new MsiInstallationSupportException(string.Format((IFormatProvider)CultureInfo.InvariantCulture, "MsiDatabaseOpenViewW returned error code {0}.", (object)windowsMessageCodes3.ToString()));
                }
                Console.WriteLine("Success: MsiDatabaseOpenViewW()");
                Program.WINDOWS_MESSAGE_CODES windowsMessageCodes4 = (Program.WINDOWS_MESSAGE_CODES)Program.MsiViewExecute(phView, num);
                if (windowsMessageCodes4 != 0)
                {
                    Console.WriteLine("Failed MsiViewExecute(), returnValue: " + windowsMessageCodes4.ToString());
                    throw new MsiInstallationSupportException(string.Format((IFormatProvider)CultureInfo.InvariantCulture, "MsiViewExecute returned error code {0}.", (object)windowsMessageCodes4.ToString()));
                }
                Console.WriteLine("Success: MsiViewExecute()");
                Program.WINDOWS_MESSAGE_CODES windowsMessageCodes5 = (Program.WINDOWS_MESSAGE_CODES)Program.MsiViewClose(phView);
                if (windowsMessageCodes5 == Program.WINDOWS_MESSAGE_CODES.ERROR_SUCCESS)
                {
                    Console.WriteLine("Success: MsiViewClose()");
                    Program.WINDOWS_MESSAGE_CODES windowsMessageCodes6 = (Program.WINDOWS_MESSAGE_CODES)Program.MsiDatabaseCommit(phDatabase);
                    if (windowsMessageCodes6 != 0)
                    {
                        Console.WriteLine("Failed MsiDatabaseCommit(), returnValue: " + windowsMessageCodes6.ToString());
                        throw new MsiInstallationSupportException(string.Format((IFormatProvider)CultureInfo.InvariantCulture, "MsiDatabaseCommit returned error code {0}.", (object)windowsMessageCodes6.ToString()));
                    }
                    Console.WriteLine("Success: MsiDatabaseCommit()");
                }
                else
                {
                    Console.WriteLine("Failed MsiViewClose(), returnValue: " + windowsMessageCodes5.ToString());
                    if (windowsMessageCodes5 != 0)
                    {
                        Console.WriteLine("Failed MsiViewClose(), returnValue: " + windowsMessageCodes5.ToString());
                        throw new MsiInstallationSupportException(string.Format((IFormatProvider)CultureInfo.InvariantCulture, "MsiViewClose returned error code {0}.", (object)windowsMessageCodes5.ToString()));
                    }
                }
            }
            finally
            {
                if (num != IntPtr.Zero)
                    Program.MsiCloseHandle(num);
                if (phView != IntPtr.Zero)
                    Program.MsiCloseHandle(phView);
                if (phDatabase != IntPtr.Zero)
                    Program.MsiCloseHandle(phDatabase);
            }
            Console.WriteLine("Finished ChangeMSIProperty()");
        }

        private enum WINDOWS_MESSAGE_CODES
        {
            ERROR_SUCCESS = 0,
            ERROR_ACCESS_DENIED = 5,
            ERROR_INVALID_HANDLE = 6,
            ERROR_INVALID_PARAMETER = 87, // 0x00000057
            ERROR_OPEN_FAILED = 110, // 0x0000006E
            ERROR_MORE_DATA = 234, // 0x000000EA
            ERROR_NO_MORE_ITEMS = 259, // 0x00000103
            ERROR_INSTALL_USEREXIT = 1602, // 0x00000642
            ERROR_INSTALL_FAILURE = 1603, // 0x00000643
            ERROR_UNKNOWN_PRODUCT = 1605, // 0x00000645
            ERROR_UNKNOWN_PROPERTY = 1608, // 0x00000648
            ERROR_INVALID_HANDLE_STATE = 1609, // 0x00000649
            ERROR_BAD_CONFIGURATION = 1610, // 0x0000064A
            ERROR_INSTALL_SOURCE_ABSENT = 1612, // 0x0000064C
            ERROR_BAD_QUERY_SYNTAX = 1615, // 0x0000064F
            ERROR_INSTALL_IN_PROGRESS = 1618, // 0x00000652
            ERROR_FUNCTION_FAILED = 1627, // 0x0000065B
            ERROR_CREATE_FAILED = 1631, // 0x0000065F
        }
    }
}
