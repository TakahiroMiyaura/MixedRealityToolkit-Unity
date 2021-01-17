// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Win32;
using UnityEditor;
using UnityEditor.WindowsStandalone;
using UnityEngine;
using UnityEngine.Events;
using Debug = UnityEngine.Debug;

namespace HoloToolkit.Unity
{
    /// <summary>
    /// Contains utility functions for building for the device
    /// </summary>
    public class BuildDeployTools
    {
        public const string DefaultMSBuildVersion = "15.0";

        public static bool CanBuild()
        {
            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.WSA) == ScriptingImplementation.IL2CPP && IsIl2CppAvailable())
            {
                return true;
            }

            return PlayerSettings.GetScriptingBackend(BuildTargetGroup.WSA) == ScriptingImplementation.WinRTDotNET && IsDotNetAvailable();
        }

        public static bool IsDotNetAvailable()
        {
            return Directory.Exists(EditorApplication.applicationContentsPath + "\\PlaybackEngines\\MetroSupport\\Managed\\UAP");
        }

        public static bool IsIl2CppAvailable()
        {
            return Directory.Exists(EditorApplication.applicationContentsPath + "\\PlaybackEngines\\MetroSupport\\Managed\\il2cpp");
        }

        /// <summary>
        /// Displays a dialog if no scenes are present in the build and returns true if build can proceed.
        /// </summary>
        /// <returns></returns>
        public static bool CheckBuildScenes()
        {
            if (EditorBuildSettings.scenes.Length == 0)
            {
                return EditorUtility.DisplayDialog("Attention!",
                    "No scenes are present in the build settings!\n\n Do you want to cancel and add one?",
                    "Continue Anyway", "Cancel Build");
            }

            return true;
        }

        /// <summary>
        /// Do a build configured for Mixed Reality Applications, returns the error from BuildPipeline.BuildPlayer
        /// </summary>
        public static bool BuildSLN()
        {
            return BuildSLN(BuildDeployPrefs.BuildDirectory, false);
        }

        public static bool BuildSLN(string buildDirectory, bool showDialog = true)
        {
            // Use BuildSLNUtilities to create the SLN
            bool buildSuccess = false;

            if (CheckBuildScenes() == false)
            {
                return false;
            }

            var buildInfo = new BuildInfo
            {
                // These properties should all match what the Standalone.proj file specifies
                OutputDirectory = buildDirectory,
                Scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path),
                BuildTarget = BuildTarget.WSAPlayer,
                WSASdk = WSASDK.UWP,
                WSAUWPBuildType = EditorUserBuildSettings.wsaUWPBuildType,
                WSAUwpSdk = EditorUserBuildSettings.wsaUWPSDK,

                // Configure a post build action that will compile the generated solution
#if UNITY_2018_1_OR_NEWER
                PostBuildAction = (innerBuildInfo, buildReport) =>
                {
                    if (buildReport.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                    {
                        EditorUtility.DisplayDialog(string.Format("{0} WindowsStoreApp Build {1}!", PlayerSettings.productName, buildReport.summary.result), "See console for details", "OK");
                    }
#else
                PostBuildAction = (innerBuildInfo, buildError) =>
                {
                    if (!string.IsNullOrEmpty(buildError))
                    {
                        EditorUtility.DisplayDialog(string.Format("{0} WindowsStoreApp Build Failed!", PlayerSettings.productName), buildError, "OK");
                    }
#endif
                    else
                    {
                        if (showDialog)
                        {
                            if (!EditorUtility.DisplayDialog(PlayerSettings.productName, "Build Complete", "OK", "Build AppX"))
                            {
                                BuildAppxFromSLN(
                                    PlayerSettings.productName,
                                    BuildDeployPrefs.MsBuildVersion,
                                    BuildDeployPrefs.ForceRebuild,
                                    BuildDeployPrefs.BuildConfig,
                                    BuildDeployPrefs.BuildPlatform,
                                    BuildDeployPrefs.BuildDirectory,
                                    BuildDeployPrefs.IncrementBuildVersion);
                            }
                        }

                        buildSuccess = true;
                    }
                }
            };

            BuildSLNUtilities.RaiseOverrideBuildDefaults(ref buildInfo);

            BuildSLNUtilities.PerformBuild(buildInfo);

            return buildSuccess;
        }

        static Boo.Lang.List<string> output = new Boo.Lang.List<string>();

        private static bool isExit = false;

        public static string CalcMSBuildPath(string msBuildVersion)
        {
            // Finding msbuild.exe involves different work depending on whether or not users
            // have VS2017 or VS2019 installed.
            foreach (VSWhereFindOption findOption in VSWhereFindOptions)
            {
                string arguments = findOption.arguments;
                if (string.IsNullOrWhiteSpace(EditorUserBuildSettings.wsaUWPVisualStudioVersion))
                {
                    arguments += " -latest";
                }
                else
                {
                    // Add version number with brackets to find only the specified version
                    arguments += $" -version [1,{EditorUserBuildSettings.wsaUWPVisualStudioVersion}]";
                }

                using (var proc = new Process())
                using (var ctoken = new CancellationTokenSource())
                {
                    proc.EnableRaisingEvents = true;
                    proc.StartInfo =
                        new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            Arguments = arguments,
                            WorkingDirectory = @"C:\Program Files (x86)\Microsoft Visual Studio\Installer"
                        };
                    output.Clear();

                    proc.OutputDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                        {
                            output.Add(args.Data);
                        }
                    };

                    proc.Exited += (sender, args) => { ctoken.Cancel(); };
                    proc.Start();
                    proc.BeginOutputReadLine();
                    ctoken.Token.WaitHandle.WaitOne();
                    proc.WaitForExit();

                    foreach (var path in output)
                    {
                        if (!string.IsNullOrEmpty(path))
                        {
                            string[] paths = path.Split(new[] {Environment.NewLine},
                                StringSplitOptions.RemoveEmptyEntries);

                            if (paths.Length > 0)
                            {
                                // if there are multiple visual studio installs,
                                // prefer enterprise, then pro, then community
                                string bestPath = paths.OrderByDescending(p => p.ToLower().Contains("enterprise"))
                                    .ThenByDescending(p => p.ToLower().Contains("professional"))
                                    .ThenByDescending(p => p.ToLower().Contains("community")).First();

                                string finalPath = $@"{bestPath}{findOption.pathSuffix}";
                                if (File.Exists(finalPath))
                                {
                                    return finalPath;
                                }
                            }
                        }
                    }
                }
            }
            Debug.LogError("Unable to find a valid path to Visual Studio Instance!");
            return string.Empty;
        }

        /// <summary>
        /// This struct controls the behavior of the arguments that are used
        /// when finding msbuild.exe.
        /// </summary>
        private struct VSWhereFindOption
        {
            public VSWhereFindOption(string args, string suffix)
            {
                arguments = args;
                pathSuffix = suffix;
            }

            /// <summary>
            /// Used to populate the Arguments of ProcessStartInfo when invoking
            /// vswhere.
            /// </summary>
            public string arguments;

            /// <summary>
            /// This string is added as a suffix to the result of the vswhere path
            /// search.
            /// </summary>
            public string pathSuffix;
        }

        private static readonly VSWhereFindOption[] VSWhereFindOptions =
        {
            // This find option corresponds to the version of vswhere that ships with VS2019.
            new VSWhereFindOption(
                @"/C vswhere -all -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe",
                ""),
            // This find option corresponds to the version of vswhere that ships with VS2017 - this doesn't have
            // support for the -find command switch.
            new VSWhereFindOption(
                @"/C vswhere -all -products * -requires Microsoft.Component.MSBuild -property installationPath",
                "\\MSBuild\\15.0\\Bin\\MSBuild.exe"),
        };

        public static bool RestoreNugetPackages(string nugetPath, string storePath)
        {
            Debug.Assert(File.Exists(nugetPath));
            Debug.Assert(Directory.Exists(storePath));

            var nugetPInfo = new ProcessStartInfo
            {
                FileName = nugetPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                Arguments = "restore \"" + storePath + "/project.json\""
            };

            using (var nugetP = new Process())
            {
                nugetP.StartInfo = nugetPInfo;
                nugetP.Start();
                nugetP.WaitForExit();
                nugetP.Close();
                nugetP.Dispose();
            }

            return File.Exists(storePath + "\\project.lock.json");
        }

        public static Queue<UnityAction> Stacks = new Queue<UnityAction>();

        public static Task<bool> BuildAppxFromSLN(string productName, string msBuildVersion, bool forceRebuildAppx, string buildConfig, string buildPlatform, string buildDirectory, bool incrementVersion, bool showDialog = true)
        {
            EditorUtility.DisplayProgressBar("Build AppX", "Building AppX Package...", 0);
            string slnFilename = Path.Combine(buildDirectory, PlayerSettings.productName + ".sln");

            if (!File.Exists(slnFilename))
            {
                Debug.LogError("Unable to find Solution to build from!");
                EditorUtility.ClearProgressBar();
                return Task.FromResult(false);
            }

            // Get and validate the msBuild path...
            var msBuildPath = CalcMSBuildPath(msBuildVersion);

            if (!File.Exists(msBuildPath))
            {
                Debug.LogErrorFormat("MSBuild.exe is missing or invalid:\n{0}.", msBuildPath);
                EditorUtility.ClearProgressBar();
                return Task.FromResult(false);
            }

            // Get the path to the NuGet tool
            string unity = Path.GetDirectoryName(EditorApplication.applicationPath);
            System.Diagnostics.Debug.Assert(unity != null, "unity != null");
            string storePath = Path.GetFullPath(Path.Combine(Path.Combine(Application.dataPath, ".."), buildDirectory));
            string solutionProjectPath = Path.GetFullPath(Path.Combine(storePath, productName + @".sln"));

            //// Bug in Unity editor that doesn't copy project.json and project.lock.json files correctly if solutionProjectPath is not in a folder named UWP.
            //if (!File.Exists(storePath + "\\project.json"))
            //{
            //    File.Copy(unity + @"\Data\PlaybackEngines\MetroSupport\Tools\project.json", storePath + "\\project.json");
            //}

            //string assemblyCSharp = string.Format("{0}/GeneratedProjects/UWP/Assembly-CSharp", storePath);
            //string assemblyCSharpFirstPass = string.Format("{0}/GeneratedProjects/UWP/Assembly-CSharp-firstpass", storePath);
            //bool restoreFirstPass = Directory.Exists(assemblyCSharpFirstPass);
            //string nugetPath = Path.Combine(unity, @"Data\PlaybackEngines\MetroSupport\Tools\NuGet.exe");

            //// Before building, need to run a nuget restore to generate a json.lock file. Failing to do this breaks the build in VS RTM
            //if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.WSA) == ScriptingImplementation.WinRTDotNET &&
            //    (!RestoreNugetPackages(nugetPath, storePath) ||
            //     !RestoreNugetPackages(nugetPath, storePath + "\\" + productName) ||
            //     EditorUserBuildSettings.wsaGenerateReferenceProjects && !RestoreNugetPackages(nugetPath, assemblyCSharp) ||
            //     EditorUserBuildSettings.wsaGenerateReferenceProjects && restoreFirstPass && !RestoreNugetPackages(nugetPath, assemblyCSharpFirstPass)))
            //{
            //    Debug.LogError("Failed to restore nuget packages");
            //    EditorUtility.ClearProgressBar();
            //    return false;
            //}

            EditorUtility.DisplayProgressBar("Build AppX", "Building AppX Package...", 25);

            // Ensure that the generated .appx version increments by modifying Package.appxmanifest
            if (!SetPackageVersion(incrementVersion))
            {
                Debug.LogError("Failed to increment package version!");
                EditorUtility.ClearProgressBar();
                return Task.FromResult(false);
            }

            // Now do the actual build
            var pInfo = new ProcessStartInfo
            {
                FileName = msBuildPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = string.Format("\"{0}\" /t:{1} /p:Configuration={2} /p:Platform={3} /verbosity:m",
                    solutionProjectPath,
                    forceRebuildAppx ? "Rebuild" : "Build",
                    buildConfig,
                    buildPlatform)
            };

            var process = new Process
            {
                StartInfo = pInfo,
                EnableRaisingEvents = true
            };

            return Task.Run<bool>((() =>
            {
                try
                {
                    process.ErrorDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            Debug.LogError(args.Data);
                        }
                    };

                    process.OutputDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            Debug.Log(args.Data);
                        }
                    };

                    if (!process.Start())
                    {
                        Stacks.Enqueue(() =>
                        {
                            Debug.LogError("Failed to start process!");
                            EditorUtility.ClearProgressBar();
                        });
                        process.Close();
                        process.Dispose();
                        return false;
                    }

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        Stacks.Enqueue(() =>
                            {
                                EditorUtility.ClearProgressBar();

                                if ( showDialog &&
                                    !EditorUtility.DisplayDialog("Build AppX", "AppX Build Successful!", "OK",
                                        "Open AppX Folder"))
                                {

                                    Process.Start("explorer.exe",
                                        string.Format("/f /open,{0}\\AppPackages\\{1}",
                                            Path.GetFullPath(BuildDeployPrefs.BuildDirectory), PlayerSettings.productName));
                                    Debug.Log(string.Format("/f /open,{0}\\AppPackages\\{1}",
                                        Path.GetFullPath(BuildDeployPrefs.BuildDirectory), PlayerSettings.productName));
                                }
                            }
                        );
                    }
                    else
                    {
                        var code = process.ExitCode;
                        Stacks.Enqueue(() =>
                        {
                            Debug.LogError(string.Format("MSBuild error (code = {0})", code));
                            EditorUtility.ClearProgressBar();
                            EditorUtility.DisplayDialog(PlayerSettings.productName + " build Failed!",
                                "Failed to build appx from solution. Error code: " + code, "OK");
                        });

                        process.Close();
                        process.Dispose();
                        return false;
                    }

                    process.Close();
                    process.Dispose();
                }
                catch (Exception e)
                {
                    process.Close();
                    process.Dispose();
                    Stacks.Enqueue(() =>
                    {
                        Debug.LogError("Cmd Process EXCEPTION: " + e);
                        EditorUtility.ClearProgressBar();
                    });
                    return false;
                }
                return true;
            }));
        }

        private static bool SetPackageVersion(bool increment)
        {
            // Find the manifest, assume the one we want is the first one
            string[] manifests = Directory.GetFiles(BuildDeployPrefs.AbsoluteBuildDirectory, "Package.appxmanifest", SearchOption.AllDirectories);

            if (manifests.Length == 0)
            {
                Debug.LogError(string.Format("Unable to find Package.appxmanifest file for build (in path - {0})", BuildDeployPrefs.AbsoluteBuildDirectory));
                return false;
            }

            string manifest = manifests[0];
            var rootNode = XElement.Load(manifest);
            var identityNode = rootNode.Element(rootNode.GetDefaultNamespace() + "Identity");

            if (identityNode == null)
            {
                Debug.LogError(string.Format("Package.appxmanifest for build (in path - {0}) is missing an <Identity /> node", BuildDeployPrefs.AbsoluteBuildDirectory));
                return false;
            }

            // We use XName.Get instead of string -> XName implicit conversion because
            // when we pass in the string "Version", the program doesn't find the attribute.
            // Best guess as to why this happens is that implicit string conversion doesn't set the namespace to empty
            var versionAttr = identityNode.Attribute(XName.Get("Version"));

            if (versionAttr == null)
            {
                Debug.LogError(string.Format("Package.appxmanifest for build (in path - {0}) is missing a version attribute in the <Identity /> node.", BuildDeployPrefs.AbsoluteBuildDirectory));
                return false;
            }

            // Assume package version always has a '.' between each number.
            // According to https://msdn.microsoft.com/en-us/library/windows/apps/br211441.aspx
            // Package versions are always of the form Major.Minor.Build.Revision.
            // Note: Revision number reserved for Windows Store, and a value other than 0 will fail WACK.
            var version = PlayerSettings.WSA.packageVersion;
            var newVersion = new Version(version.Major, version.Minor, increment ? version.Build + 1 : version.Build, version.Revision);

            PlayerSettings.WSA.packageVersion = newVersion;
            versionAttr.Value = newVersion.ToString();
            rootNode.Save(manifest);
            return true;
        }
    }
}
