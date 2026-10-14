using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace HospitalModUpdateChecker.Bootstrap
{
    public static class HospitalModUpdateBootstrap
    {
        private const string PluginFolderName =
            "Loupito75.HospitalModUpdateChecker";

        private const string CoreFileName =
            "Loupito75.HospitalModUpdateChecker.Bootstrap.Core.dll";

        private const string CoreTypeName =
            "HospitalModUpdateChecker.Bootstrap.HospitalModUpdateBootstrapCore";

        public static IEnumerable<string> TargetDLLs
        {
            get { return new string[0]; }
        }

        public static void Initialize()
        {
            ManualLogSource log =
                Logger.CreateLogSource(
                    "   Loupito75:HospitalModUpdateCheckerBootstrapLoader");

            try
            {
                string corePath =
                    Path.Combine(
                        Path.Combine(
                            Path.Combine(
                                Paths.BepInExRootPath,
                                "plugins"),
                            PluginFolderName),
                        CoreFileName);

                if (!File.Exists(corePath))
                {
                    log.LogWarning(
                        "HMUC bootstrap core is missing; no pending update can be applied.");
                    return;
                }

                byte[] coreBytes =
                    File.ReadAllBytes(corePath);

                Assembly coreAssembly =
                    Assembly.Load(coreBytes);

                Type coreType =
                    coreAssembly.GetType(
                        CoreTypeName,
                        true);

                MethodInfo run =
                    coreType.GetMethod(
                        "Run",
                        BindingFlags.Public |
                        BindingFlags.Static);

                if (run == null)
                {
                    throw new MissingMethodException(
                        CoreTypeName,
                        "Run");
                }

                run.Invoke(
                    null,
                    null);
            }
            catch (TargetInvocationException exception)
            {
                Exception failure =
                    exception.InnerException ??
                    exception;

                log.LogError(
                    "HMUC bootstrap core failed safely before plugin loading: " +
                    failure);
            }
            catch (Exception exception)
            {
                log.LogError(
                    "HMUC bootstrap loader failed safely before plugin loading: " +
                    exception);
            }
            finally
            {
                Logger.Sources.Remove(log);
            }
        }

        public static void Patch(
            ref AssemblyDefinition assembly)
        {
        }

        public static void Finish()
        {
        }
    }
}
