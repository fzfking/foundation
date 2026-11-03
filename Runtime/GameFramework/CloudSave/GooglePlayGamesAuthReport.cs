using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Foundation.GameFramework.CloudSave
{
    public enum GooglePlayGamesAuthStage
    {
        /// <summary>The automatic sign-in the bootstrap scene starts; shows no UI.</summary>
        Silent = 0,
        /// <summary>The silent sign-in did not settle within the authentication timeout.</summary>
        SilentTimeout = 1,
        /// <summary>The sign-in prompt the save load falls back to when nothing was cached.</summary>
        Interactive = 2,
        /// <summary>The player switched cloud saving on and the sign-in was asked for from Settings.</summary>
        Manual = 3,
        /// <summary>No sign-in was tried because the device reported no internet connection.</summary>
        SkippedOffline = 4,
    }

    /// <summary>
    /// What one Play Games sign-in attempt did and what the device looked like. The SDK only says Success,
    /// InternalError or Canceled, which cannot tell a missing certificate from outdated Play Services, so the
    /// report adds the checks that usually decide it.
    /// </summary>
    public sealed class GooglePlayGamesAuthReport
    {
        public GooglePlayGamesAuthStage Stage { get; }

        /// <summary>The SDK's SignInStatus name, "Timeout", "Offline" or "Exception".</summary>
        public string Status { get; }

        public long ElapsedMilliseconds { get; }

        /// <summary>The exception message when the attempt threw, otherwise null.</summary>
        public string Detail { get; }

        public GooglePlayGamesDiagnostics Diagnostics { get; }

        public bool Succeeded => string.Equals(Status, "Success", StringComparison.Ordinal);

        public GooglePlayGamesAuthReport(
            GooglePlayGamesAuthStage stage,
            string status,
            long elapsedMilliseconds,
            string detail,
            GooglePlayGamesDiagnostics diagnostics)
        {
            Stage = stage;
            Status = status ?? string.Empty;
            ElapsedMilliseconds = elapsedMilliseconds;
            Detail = detail;
            Diagnostics = diagnostics;
        }

        public Dictionary<string, object> ToParameters()
        {
            var parameters = new Dictionary<string, object>(12, StringComparer.Ordinal)
            {
                ["stage"] = Stage.ToString(),
                ["status"] = Status,
                ["elapsed_ms"] = ElapsedMilliseconds,
            };
            if (!string.IsNullOrEmpty(Detail))
                parameters["detail"] = Detail;
            Diagnostics?.AddTo(parameters);
            return parameters;
        }

        public override string ToString()
        {
            var builder = new StringBuilder(160);
            builder.Append("stage=").Append(Stage).Append(" status=").Append(Status)
                .Append(" elapsed=").Append(ElapsedMilliseconds).Append("ms");
            if (!string.IsNullOrEmpty(Detail))
                builder.Append(" detail=").Append(Detail);
            if (Diagnostics != null)
                builder.Append(' ').Append(Diagnostics);
            return builder.ToString();
        }
    }

    /// <summary>
    /// The device-side facts behind most Play Games sign-in failures, read when an attempt finishes. Every
    /// lookup is guarded on its own: a failed one is reported as "unknown" and never throws.
    /// </summary>
    public sealed class GooglePlayGamesDiagnostics
    {
        private const string Unknown = "unknown";

        // Failures the lookups hit while collecting; the sign-in that asked for them logs each one.
        public List<string> Warnings { get; } = new();

        // com.google.android.gms.common.ConnectionResult codes worth naming; anything else stays numeric.
        private static readonly Dictionary<int, string> ConnectionResultNames = new()
        {
            [0] = "SUCCESS",
            [1] = "SERVICE_MISSING",
            [2] = "SERVICE_VERSION_UPDATE_REQUIRED",
            [3] = "SERVICE_DISABLED",
            [9] = "SERVICE_INVALID",
            [18] = "SERVICE_UPDATING",
            [19] = "SERVICE_MISSING_PERMISSION",
        };

        public string InternetReachability { get; private set; } = Unknown;

        /// <summary>The Google Play services availability check, for example SUCCESS or SERVICE_VERSION_UPDATE_REQUIRED.</summary>
        public string PlayServices { get; private set; } = Unknown;

        /// <summary>SHA-1 of the certificate the installed app is signed with; compare it with the Play Console credential.</summary>
        public string SigningSha1 { get; private set; } = Unknown;

        /// <summary>The store package that installed the app, "com.android.vending" for Google Play.</summary>
        public string Installer { get; private set; } = Unknown;

        public string AppVersion { get; private set; } = Unknown;

        public string Device { get; private set; } = Unknown;

        public string OperatingSystem { get; private set; } = Unknown;

        private GooglePlayGamesDiagnostics()
        {
        }

        public static GooglePlayGamesDiagnostics Collect()
        {
            var diagnostics = new GooglePlayGamesDiagnostics();
            try
            {
                diagnostics.InternetReachability = Application.internetReachability.ToString();
                diagnostics.AppVersion = Application.version;
                diagnostics.Device = SystemInfo.deviceModel;
                diagnostics.OperatingSystem = SystemInfo.operatingSystem;
            }
            catch (Exception exception)
            {
                diagnostics.Warnings.Add($"basic device info failed: {exception.Message}");
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            diagnostics.CollectAndroid();
#endif
            return diagnostics;
        }

        public void AddTo(IDictionary<string, object> parameters)
        {
            parameters["internet"] = InternetReachability;
            parameters["play_services"] = PlayServices;
            parameters["signing_sha1"] = SigningSha1;
            parameters["installer"] = Installer;
            parameters["app_version"] = AppVersion;
            parameters["device"] = Device;
            parameters["os"] = OperatingSystem;
        }

        public override string ToString()
            => $"internet={InternetReachability} playServices={PlayServices} installer={Installer} " +
               $"signingSha1={SigningSha1} app={AppVersion} device={Device} os={OperatingSystem}";

#if UNITY_ANDROID && !UNITY_EDITOR
        private void CollectAndroid()
        {
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                PlayServices = ReadPlayServices(activity);
                using var packageManager = activity.Call<AndroidJavaObject>("getPackageManager");
                var packageName = activity.Call<string>("getPackageName");
                SigningSha1 = ReadSigningSha1(packageManager, packageName);
                Installer = ReadInstaller(packageManager, packageName);
            }
            catch (Exception exception)
            {
                Warnings.Add($"Android lookups failed: {exception.Message}");
            }
        }

        private static string ReadPlayServices(AndroidJavaObject activity)
        {
            try
            {
                using var availabilityClass = new AndroidJavaClass("com.google.android.gms.common.GoogleApiAvailability");
                using var availability = availabilityClass.CallStatic<AndroidJavaObject>("getInstance");
                var code = availability.Call<int>("isGooglePlayServicesAvailable", activity);
                return ConnectionResultNames.TryGetValue(code, out var name) ? name : "code_" + code;
            }
            catch (Exception exception)
            {
                return "error:" + exception.Message;
            }
        }

        private static string ReadSigningSha1(AndroidJavaObject packageManager, string packageName)
        {
            try
            {
                // GET_SIGNING_CERTIFICATES (API 28) first, GET_SIGNATURES below it.
                const int getSigningCertificates = 0x08000000;
                using var info = packageManager.Call<AndroidJavaObject>("getPackageInfo", packageName, getSigningCertificates);
                using var signingInfo = info.Get<AndroidJavaObject>("signingInfo");
                var signers = signingInfo.Call<AndroidJavaObject[]>("getApkContentsSigners");
                return Sha1Of(signers[0].Call<sbyte[]>("toByteArray"));
            }
            catch (Exception)
            {
                try
                {
                    const int getSignatures = 0x40;
                    using var info = packageManager.Call<AndroidJavaObject>("getPackageInfo", packageName, getSignatures);
                    var signatures = info.Get<AndroidJavaObject[]>("signatures");
                    return Sha1Of(signatures[0].Call<sbyte[]>("toByteArray"));
                }
                catch (Exception exception)
                {
                    return "error:" + exception.Message;
                }
            }
        }

        private static string ReadInstaller(AndroidJavaObject packageManager, string packageName)
        {
            try
            {
                return packageManager.Call<string>("getInstallerPackageName", packageName) ?? "none";
            }
            catch (Exception exception)
            {
                return "error:" + exception.Message;
            }
        }

        private static string Sha1Of(sbyte[] certificate)
        {
            var bytes = new byte[certificate.Length];
            Buffer.BlockCopy(certificate, 0, bytes, 0, bytes.Length);
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(bytes);
            var builder = new StringBuilder(hash.Length * 3);
            for (var index = 0; index < hash.Length; index++)
            {
                if (index > 0)
                    builder.Append(':');
                builder.Append(hash[index].ToString("X2"));
            }

            return builder.ToString();
        }
#endif
    }
}
