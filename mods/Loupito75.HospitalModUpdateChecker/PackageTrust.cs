using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace HospitalModUpdateChecker
{
    internal static class PackageTrust
    {
        internal const string PublicKeyFileName =
            "Loupito75.HospitalModUpdateChecker.PackagePublicKey.xml";

        internal const string AlgorithmName =
            "RSASSA-PKCS1-v1_5-SHA256";

        private const int MaximumSignatureFileBytes = 16384;
        private const int MaximumKeyIdLength = 128;

        internal static int SignatureFileMaximumBytes
        {
            get { return MaximumSignatureFileBytes; }
        }

        internal static bool IsConfigured(
            out string error)
        {
            string keyId;
            RSAParameters parameters;

            return TryLoadPublicKey(
                out keyId,
                out parameters,
                out error);
        }

        internal static bool TryVerify(
            byte[] manifestBytes,
            byte[] signatureBytes,
            out string error)
        {
            error = null;

            if (manifestBytes == null ||
                manifestBytes.Length == 0)
            {
                error = "Package manifest bytes are empty.";
                return false;
            }

            if (signatureBytes == null ||
                signatureBytes.Length == 0 ||
                signatureBytes.Length >
                    MaximumSignatureFileBytes)
            {
                error = "Package signature file is empty or too large.";
                return false;
            }

            string trustedKeyId;
            RSAParameters publicParameters;

            if (!TryLoadPublicKey(
                    out trustedKeyId,
                    out publicParameters,
                    out error))
            {
                return false;
            }

            string signatureText;

            try
            {
                UTF8Encoding utf8 =
                    new UTF8Encoding(
                        false,
                        true);

                signatureText =
                    utf8.GetString(
                        signatureBytes);
            }
            catch (Exception exception)
            {
                error =
                    "Package signature file is not valid UTF-8: " +
                    exception.GetType().Name;
                return false;
            }

            SignatureEnvelope envelope;

            if (!SignatureEnvelope.TryParse(
                    signatureText,
                    out envelope,
                    out error))
            {
                return false;
            }

            if (!string.Equals(
                    envelope.KeyId,
                    trustedKeyId,
                    StringComparison.Ordinal))
            {
                error =
                    "Package signature key ID is not trusted.";
                return false;
            }

            if (!string.Equals(
                    envelope.Algorithm,
                    AlgorithmName,
                    StringComparison.Ordinal))
            {
                error =
                    "Package signature algorithm is not supported.";
                return false;
            }

            byte[] signature;

            try
            {
                signature =
                    Convert.FromBase64String(
                        envelope.Signature);
            }
            catch
            {
                error =
                    "Package signature is not valid Base64.";
                return false;
            }

            if (signature.Length < 256 ||
                signature.Length > 512)
            {
                error =
                    "Package signature has an unexpected size.";
                return false;
            }

            byte[] hash;

            using (SHA256Managed sha =
                new SHA256Managed())
            {
                hash =
                    sha.ComputeHash(
                        manifestBytes);
            }

            try
            {
                CspParameters csp =
                    new CspParameters();

                csp.ProviderType = 24;

                using (RSACryptoServiceProvider rsa =
                    new RSACryptoServiceProvider(
                        3072,
                        csp))
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.ImportParameters(
                        publicParameters);

                    RSAPKCS1SignatureDeformatter verifier =
                        new RSAPKCS1SignatureDeformatter(
                            rsa);

                    verifier.SetHashAlgorithm(
                        "SHA256");

                    if (!verifier.VerifySignature(
                            hash,
                            signature))
                    {
                        error =
                            "Package manifest signature is invalid.";
                        return false;
                    }
                }
            }
            catch (Exception exception)
            {
                error =
                    "Package manifest signature verification failed: " +
                    exception.GetType().Name +
                    ": " +
                    exception.Message;
                return false;
            }

            return true;
        }

        private static bool TryLoadPublicKey(
            out string keyId,
            out RSAParameters parameters,
            out string error)
        {
            keyId = null;
            parameters =
                new RSAParameters();
            error = null;

            string directory =
                Path.GetDirectoryName(
                    typeof(Plugin).Assembly.Location);

            string path =
                Path.Combine(
                    directory ?? string.Empty,
                    PublicKeyFileName);

            if (!File.Exists(path))
            {
                error =
                    "HMUC package public key is missing.";
                return false;
            }

            try
            {
                XmlDocument document =
                    new XmlDocument();

                document.XmlResolver = null;
                document.Load(path);

                if (document.DocumentType != null)
                {
                    error =
                        "HMUC package public key contains a forbidden DTD.";
                    return false;
                }

                XmlElement root =
                    document.DocumentElement;

                if (root == null ||
                    root.Name !=
                        "HMUCPublicKey" ||
                    root.GetAttribute(
                        "formatVersion") != "1" ||
                    root.GetAttribute(
                        "configured") != "true" ||
                    root.GetAttribute(
                        "algorithm") !=
                        AlgorithmName)
                {
                    error =
                        "HMUC package public key is not configured.";
                    return false;
                }

                keyId =
                    root.GetAttribute(
                        "keyId");

                if (string.IsNullOrEmpty(keyId) ||
                    keyId.Length >
                        MaximumKeyIdLength)
                {
                    error =
                        "HMUC package public key has an invalid key ID.";
                    return false;
                }

                byte[] modulus;
                byte[] exponent;

                if (!TryReadBase64Element(
                        root,
                        "Modulus",
                        256,
                        512,
                        out modulus) ||
                    !TryReadBase64Element(
                        root,
                        "Exponent",
                        1,
                        8,
                        out exponent))
                {
                    error =
                        "HMUC package public key is malformed.";
                    return false;
                }

                parameters.Modulus =
                    modulus;

                parameters.Exponent =
                    exponent;

                return true;
            }
            catch (Exception exception)
            {
                error =
                    "HMUC package public key could not be read: " +
                    exception.GetType().Name +
                    ": " +
                    exception.Message;
                return false;
            }
        }

        private static bool TryReadBase64Element(
            XmlElement root,
            string name,
            int minimumBytes,
            int maximumBytes,
            out byte[] value)
        {
            value = null;

            XmlElement element =
                root[name];

            if (element == null)
            {
                return false;
            }

            string text =
                (element.InnerText ??
                 string.Empty)
                .Trim();

            if (text.Length == 0 ||
                text.Length >
                    maximumBytes * 2)
            {
                return false;
            }

            try
            {
                byte[] decoded =
                    Convert.FromBase64String(
                        text);

                if (decoded.Length <
                        minimumBytes ||
                    decoded.Length >
                        maximumBytes)
                {
                    return false;
                }

                value = decoded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class SignatureEnvelope
        {
            internal string KeyId;
            internal string Algorithm;
            internal string Signature;

            internal static bool TryParse(
                string text,
                out SignatureEnvelope envelope,
                out string error)
            {
                envelope = null;
                error = null;

                object rootValue;

                if (!SimpleJsonParser.TryParse(
                        text,
                        out rootValue,
                        out error))
                {
                    return false;
                }

                Dictionary<string, object> root =
                    rootValue
                    as Dictionary<string, object>;

                if (root == null)
                {
                    error =
                        "Package signature root must be a JSON object.";
                    return false;
                }

                foreach (string key in root.Keys)
                {
                    if (key != "formatVersion" &&
                        key != "keyId" &&
                        key != "algorithm" &&
                        key != "signature")
                    {
                        error =
                            "Unknown package signature property '" +
                            key +
                            "'.";
                        return false;
                    }
                }

                object versionValue;

                if (!root.TryGetValue(
                        "formatVersion",
                        out versionValue) ||
                    !(versionValue is long) ||
                    (long)versionValue != 1L)
                {
                    error =
                        "Unsupported package signature formatVersion.";
                    return false;
                }

                string keyId;
                string algorithm;
                string signature;

                if (!TryGetString(
                        root,
                        "keyId",
                        out keyId) ||
                    !TryGetString(
                        root,
                        "algorithm",
                        out algorithm) ||
                    !TryGetString(
                        root,
                        "signature",
                        out signature))
                {
                    error =
                        "Package signature fields are invalid.";
                    return false;
                }

                if (keyId.Length == 0 ||
                    keyId.Length >
                        MaximumKeyIdLength ||
                    algorithm.Length == 0 ||
                    algorithm.Length > 64 ||
                    signature.Length < 128 ||
                    signature.Length > 1024)
                {
                    error =
                        "Package signature fields are outside allowed bounds.";
                    return false;
                }

                envelope =
                    new SignatureEnvelope();

                envelope.KeyId =
                    keyId;

                envelope.Algorithm =
                    algorithm;

                envelope.Signature =
                    signature;

                return true;
            }

            private static bool TryGetString(
                Dictionary<string, object> values,
                string key,
                out string value)
            {
                value = null;

                object raw;

                if (!values.TryGetValue(
                        key,
                        out raw))
                {
                    return false;
                }

                value =
                    raw as string;

                return value != null;
            }
        }
    }
}
