using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Foundation.Analytics;
using NUnit.Framework;

namespace Foundation.Tests.Analytics
{
    public sealed class AnalyticsSdkGateTests
    {
        [Test]
        public void AppMetricaJson_EscapesEveryControlCharacter()
        {
            var parameters = new Dictionary<string, object> { ["value"] = "a\u0001\u0008\u001f\n" };
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Foundation.GameFramework.Analytics.AppMetrica.AppMetricaAnalyticsProviderBackend"))
                .FirstOrDefault(candidate => candidate != null);
            if (type == null)
                Assert.Ignore("The optional AppMetrica adapter is not included in this test setup.");
            var method = type.GetMethod("ToJsonValue", BindingFlags.NonPublic | BindingFlags.Static);

            var json = (string)method.Invoke(null, new object[] { parameters });

            Assert.That(json, Is.EqualTo("{\"value\":\"a\\u0001\\u0008\\u001f\\n\"}"));
        }

        [Test]
        public void Revoke_BeforeAnySdkStarted_TouchesNoSdk()
        {
            var sdk = new FakeSdk();
            var gate = new AnalyticsSdkGate(new[] { sdk });

            gate.Apply(false);

            Assert.That(sdk.Calls, Is.Empty);
        }

        [Test]
        public void StartIfGranted_WithoutConsent_StartsNothing()
        {
            var sdk = new FakeSdk();
            var gate = new AnalyticsSdkGate(new[] { sdk });

            gate.StartIfGranted(false);

            Assert.That(sdk.IsStarted, Is.False);
            Assert.That(sdk.Calls, Is.Empty);
        }

        [Test]
        public void Revoke_AfterStart_StopsSendingInEveryStartedSdk()
        {
            var first = new FakeSdk();
            var second = new FakeSdk();
            var gate = new AnalyticsSdkGate(new[] { first, second });
            gate.StartIfGranted(true);

            gate.Apply(false);

            Assert.That(first.SendingEnabled, Is.False);
            Assert.That(second.SendingEnabled, Is.False);
        }

        [Test]
        public void Grant_AfterRevoke_ResumesSendingWithoutStartingTheSdkAgain()
        {
            var sdk = new FakeSdk();
            var gate = new AnalyticsSdkGate(new[] { sdk });
            gate.StartIfGranted(true);
            gate.Apply(false);

            gate.Apply(true);

            Assert.That(sdk.SendingEnabled, Is.True);
            Assert.That(sdk.StartCount, Is.EqualTo(1));
        }

        [Test]
        public void StartIfGranted_CalledRepeatedly_StartsEachSdkOnce()
        {
            var sdk = new FakeSdk();
            var gate = new AnalyticsSdkGate(new[] { sdk });

            gate.StartIfGranted(true);
            gate.StartIfGranted(true);

            Assert.That(sdk.StartCount, Is.EqualTo(1));
        }

        [Test]
        public void StartIfGranted_OneSdkThrows_TheOthersStillStartAndTheFailureIsReported()
        {
            var failing = new FakeSdk { StartFailure = new InvalidOperationException("native start failed") };
            var healthy = new FakeSdk();
            var reported = new List<Exception>();
            var gate = new AnalyticsSdkGate(new[] { failing, healthy }, reported.Add);

            gate.StartIfGranted(true);

            Assert.That(failing.IsStarted, Is.False);
            Assert.That(healthy.IsStarted, Is.True);
            Assert.That(healthy.SendingEnabled, Is.True);
            Assert.That(reported, Has.Count.EqualTo(1));
        }

        [Test]
        public void Revoke_OneSdkThrows_TheOthersStillStopSending()
        {
            var failing = new FakeSdk { RevokeFailure = new InvalidOperationException("stop failed") };
            var healthy = new FakeSdk();
            var reported = new List<Exception>();
            var gate = new AnalyticsSdkGate(new[] { failing, healthy }, reported.Add);
            gate.StartIfGranted(true);

            gate.Apply(false);

            Assert.That(healthy.SendingEnabled, Is.False);
            Assert.That(reported, Has.Count.EqualTo(1));
        }

        private sealed class FakeSdk : IAnalyticsSdk
        {
            public bool IsStarted { get; private set; }
            public bool SendingEnabled { get; private set; }
            public int StartCount { get; private set; }
            public List<string> Calls { get; } = new();
            public Exception StartFailure { get; set; }
            public Exception RevokeFailure { get; set; }

            public void Start()
            {
                StartCount++;
                if (StartFailure != null)
                    throw StartFailure;

                IsStarted = true;
                Calls.Add("start");
            }

            public void SetSendingEnabled(bool enabled)
            {
                if (!enabled && RevokeFailure != null)
                    throw RevokeFailure;

                SendingEnabled = enabled;
                Calls.Add($"sending {enabled}");
            }
        }
    }
}
