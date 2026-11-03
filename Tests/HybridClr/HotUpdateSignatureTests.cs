using System;
using System.Security.Cryptography;
using Foundation.Infrastructure.HybridClr;
using NUnit.Framework;
using System.Text;

namespace Foundation.Tests.HybridClr
{
    /// <summary>
    /// Downloaded code runs only with the build's signature: whoever controls the content server cannot swap an assembly,
    /// edit one, or serve one signed with another key. The keys here are made for the test; a real private key
    /// never enters the repository.
    /// </summary>
    public sealed class HotUpdateSignatureTests
    {
        private static readonly byte[] Assembly = { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00 };

        [Test]
        public void ASignedAssembly_VerifiesToItsOwnBytes()
        {
            var (privateKey, verifier) = NewKey();

            var payload = verifier.Verify("module", HotUpdateSignature.Sign(Assembly, privateKey));

            Assert.That(payload, Is.EqualTo(Assembly));
        }

        [Test]
        public void TheSignedForm_StartsWithTheFormatMarker()
        {
            var (privateKey, _) = NewKey();

            var signed = HotUpdateSignature.Sign(Assembly, privateKey);

            Assert.That(Encoding.ASCII.GetString(signed, 0, 6), Is.EqualTo("FDSIG1"));
        }

        [Test]
        public void AnAssemblyChangedAfterSigning_IsRefused()
        {
            var (privateKey, verifier) = NewKey();
            var signed = HotUpdateSignature.Sign(Assembly, privateKey);
            signed[signed.Length - 1] ^= 0xFF;

            Assert.Throws<HotUpdateSignatureException>(() => verifier.Verify("module", signed));
        }

        [Test]
        public void AnUnsignedAssembly_IsRefused()
        {
            var (_, verifier) = NewKey();

            Assert.Throws<HotUpdateSignatureException>(() => verifier.Verify("module", Assembly));
        }

        [Test]
        public void AnAssemblySignedWithAnotherKey_IsRefused()
        {
            var (_, verifier) = NewKey();
            var (otherKey, _) = NewKey();

            Assert.Throws<HotUpdateSignatureException>(
                () => verifier.Verify("module", HotUpdateSignature.Sign(Assembly, otherKey)));
        }

        private static (RSAParameters PrivateKey, IHotUpdateAssemblyVerifier Verifier) NewKey()
        {
            using var rsa = new RSACryptoServiceProvider(2048);
            var privateKey = rsa.ExportParameters(true);
            var verifier = new RsaHotUpdateAssemblyVerifier(
                Convert.ToBase64String(privateKey.Modulus), Convert.ToBase64String(privateKey.Exponent));
            return (privateKey, verifier);
        }
    }
}
