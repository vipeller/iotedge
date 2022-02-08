// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Util
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Edge.Util.Edged;
    using Microsoft.Extensions.Logging;

    public static class CertificateHelper
    {
        // The private-key import on windows randomly seems failing, however according to the tests, the second time
        // after a failure it usually works. The number below is just a "big enough" number randomly chosen for
        // self-healing, but gives a limit to avoid endless try.
        const int MaxCertImportRetryCount = 10;

        static Oid oidRsaEncryption = Oid.FromFriendlyName("RSA", OidGroup.All);
        static Oid oidEcPublicKey = Oid.FromFriendlyName("ECC", OidGroup.All);

        public static string GetSha256Thumbprint(X509Certificate2 cert)
        {
            Preconditions.CheckNotNull(cert);
            using (var sha256 = new SHA256Managed())
            {
                byte[] hash = sha256.ComputeHash(cert.RawData);
                return ToHexString(hash);
            }
        }

        public static (IList<X509Certificate2>, Option<string>) BuildCertificateList(X509Certificate2 cert, Option<IList<X509Certificate2>> additionalCACertificates)
        {
            var chain = new X509Chain
            {
                ChainPolicy =
                {
                    // For performance reasons do not check revocation status.
                    RevocationMode = X509RevocationMode.NoCheck,
                    // Does not check revocation status of the root certificate (sounds like it is meaningless with the option above - ask Simon or Alex)
                    RevocationFlag = X509RevocationFlag.ExcludeRoot,
                    // Certificate Authority can be unknown if it is not issued directly by a well-known CA
                    VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority
                }
            };

            if (additionalCACertificates.HasValue)
            {
                foreach (X509Certificate2 additionalCertificate in additionalCACertificates.GetOrElse(new List<X509Certificate2>()))
                {
                    if (additionalCertificate != null)
                    {
                        chain.ChainPolicy.ExtraStore.Add(additionalCertificate);
                    }
                }
            }

            try
            {
                bool chainBuildSucceeded = chain.Build(cert);
                X509ChainStatusFlags flags = X509ChainStatusFlags.UntrustedRoot | X509ChainStatusFlags.PartialChain;
                List<X509ChainStatus> filteredStatus = chain.ChainStatus.Where(cs => !flags.HasFlag(cs.Status)).ToList();
                if (!chainBuildSucceeded || filteredStatus.Count > 0)
                {
                    string errors = $"Certificate with subject: {cert.Subject} failed with errors: ";
                    string s = filteredStatus
                        .Select(c => c.StatusInformation)
                        .Aggregate(errors, (prev, curr) => $"{prev} {curr}");
                    return (new List<X509Certificate2>(), Option.Some(s));
                }

                IList<X509Certificate2> chainElements = GetCertificatesFromChain(chain);
                return (chainElements, Option.None<string>());
            }
            finally
            {
                chain.Reset();
            }
        }

        public static IList<X509Certificate2> GetCertificatesFromChain(X509Chain chainCert) =>
            chainCert.ChainElements.Cast<X509ChainElement>().Select(element => element.Certificate).ToList();

        public static (bool, Option<string>) ValidateCert(X509Certificate2 remoteCertificate, IList<X509Certificate2> remoteCertificateChain, Option<IList<X509Certificate2>> trustedCACerts)
        {
            Preconditions.CheckNotNull(remoteCertificate);
            Preconditions.CheckNotNull(remoteCertificateChain);
            Preconditions.CheckNotNull(trustedCACerts);

            (IList<X509Certificate2> remoteCerts, Option<string> errors) = BuildCertificateList(remoteCertificate, Option.Some(remoteCertificateChain));
            if (errors.HasValue)
            {
                return (false, errors);
            }

            (bool, Option<string>) result = trustedCACerts.Map(
                    caList =>
                    {
                        bool match = false;
                        foreach (X509Certificate2 chainElement in remoteCerts)
                        {
                            string thumbprint = GetSha256Thumbprint(chainElement);
                            if (remoteCertificateChain.Any(cert => GetSha256Thumbprint(cert) == thumbprint) &&
                                caList.Any(cert => GetSha256Thumbprint(cert) == thumbprint))
                            {
                                match = true;
                                break;
                            }
                        }

                        return match
                            ? (true, Option.None<string>())
                            : (false, Option.Some($"Error validating cert with Subject: {remoteCertificate.SubjectName} Thumbprint: {GetSha256Thumbprint(remoteCertificate)}"));
                    })
                .GetOrElse(() => (true, Option.None<string>()));

            return result;
        }

        public static bool ValidateCommonName(X509Certificate2 certificate, string commonName)
        {
            Preconditions.CheckNotNull(certificate);
            Preconditions.CheckNotNull(commonName);

            return GetCommonNameFromSubject(certificate.Subject)
                .Map(subject => commonName.Equals(subject, StringComparison.Ordinal))
                .GetOrElse(() => false);
        }

        public static bool ValidateCertificateThumbprint(X509Certificate2 certificate, IList<string> thumbprints)
        {
            Preconditions.CheckNotNull(certificate);
            Preconditions.CheckNotNull(thumbprints);

            var thumbprintSha1 = certificate.Thumbprint;
            var thumbprintSha256 = GetSha256Thumbprint(certificate);

            return thumbprints.Any(th =>
                        thumbprintSha1.Equals(th, StringComparison.OrdinalIgnoreCase)
                     || thumbprintSha256.Equals(th, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsCACertificate(X509Certificate2 certificate)
        {
            // https://tools.ietf.org/html/rfc3280#section-4.2.1.3
            // The keyCertSign bit is asserted when the subject public key is
            // used for verifying a signature on public key certificates.  If the
            // keyCertSign bit is asserted, then the cA bit in the basic
            // constraints extension (section 4.2.1.10) MUST also be asserted.

            // https://tools.ietf.org/html/rfc3280#section-4.2.1.10
            // The cA boolean indicates whether the certified public key belongs to
            // a CA.  If the cA boolean is not asserted, then the keyCertSign bit in
            // the key usage extension MUST NOT be asserted.
            var basicConstraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>();
            if (basicConstraints != null)
            {
                foreach (X509BasicConstraintsExtension extension in basicConstraints)
                {
                    if (extension.CertificateAuthority)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool ValidateClientCert(X509Certificate2 certificate, IList<X509Certificate2> certificateChain, Option<IList<X509Certificate2>> trustedCACerts, ILogger logger)
        {
            Preconditions.CheckNotNull(certificate);
            Preconditions.CheckNotNull(certificateChain);
            Preconditions.CheckNotNull(trustedCACerts);

            if (!ValidateCertExpiry(certificate, logger))
            {
                return false;
            }

            if (IsCACertificate(certificate))
            {
                logger?.LogWarning($"Certificate with subject: {certificate.Subject} was found to be a CA certificate, this is not permitted per the authentication policy");
                return false;
            }

            (bool result, Option<string> errors) = ValidateCert(certificate, certificateChain, trustedCACerts);
            errors.ForEach(v => logger?.LogWarning(v));

            return result;
        }

        public static bool ValidateCertExpiry(X509Certificate2 certificate, ILogger logger)
        {
            Preconditions.CheckNotNull(certificate);

            DateTime currentTime = DateTime.Now;

            if (certificate.NotAfter < currentTime)
            {
                logger?.LogWarning($"Certificate with subject: {certificate.Subject} has expired on UTC time: {certificate.NotAfter.ToString("MM-dd-yyyy H:mm:ss")}");
                return false;
            }

            if (certificate.NotBefore > currentTime)
            {
                logger?.LogWarning($"Certificate with subject: {certificate.Subject} is not valid until UTC time: {certificate.NotBefore.ToString("MM-dd-yyyy H:mm:ss")}");
                return false;
            }

            return true;
        }

        public static void InstallCertificates(IEnumerable<X509Certificate2> certificateChain, ILogger logger)
        {
            X509Certificate2[] certs = Preconditions.CheckNotNull(certificateChain, nameof(certificateChain)).ToArray();
            Preconditions.CheckNotNull(logger, nameof(logger));

            StoreName storeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StoreName.CertificateAuthority : StoreName.Root;

            logger.LogInformation($"Installing certificates {string.Join(",", certs.Select(c => $"[{c.Subject}:{c.GetExpirationDateString()}]"))} to {storeName}");
            using (var store = new X509Store(storeName, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadWrite);
                foreach (X509Certificate2 cert in certs)
                {
                    store.Add(cert);
                }
            }
        }

        public static IEnumerable<X509Certificate2> ExtractCertsFromPem(string certPath)
        {
            if (string.IsNullOrWhiteSpace(certPath) || !File.Exists(certPath))
            {
                throw new ArgumentException($"'{certPath}' is not a path to a certificate collection file");
            }

            using (var sr = new StreamReader(certPath))
            {
                return GetCertificatesFromPem(ParsePemCerts(sr.ReadToEnd()));
            }
        }

        public static IEnumerable<X509Certificate2> GetCertificatesFromPem(IEnumerable<string> rawPemCerts) =>
            rawPemCerts
                .Select(c => Encoding.UTF8.GetBytes(c))
                .Select(c => new X509Certificate2(c))
                .ToList();

        public static async Task<(X509Certificate2 ServerCertificate, IEnumerable<X509Certificate2> CertificateChain)> GetServerCertificatesFromEdgelet(Uri workloadUri, string workloadApiVersion, string workloadClientApiVersion, string moduleId, string moduleGenerationId, string edgeHubHostname, DateTime expiration, ILogger logger)
        {
            if (string.IsNullOrEmpty(edgeHubHostname))
            {
                throw new InvalidOperationException($"{nameof(edgeHubHostname)} is required.");
            }

            ServerCertificateResponse response = await new WorkloadClient(workloadUri, workloadApiVersion, workloadClientApiVersion, moduleId, moduleGenerationId).CreateServerCertificateAsync(edgeHubHostname, expiration);
            return ParseCertificateResponse(response, logger);
        }

        public static async Task<IEnumerable<X509Certificate2>> GetTrustBundleFromEdgelet(Uri workloadUri, string workloadApiVersion, string workloadClientApiVersion, string moduleId, string moduleGenerationId)
        {
            string response = await new WorkloadClient(workloadUri, workloadApiVersion, workloadClientApiVersion, moduleId, moduleGenerationId).GetTrustBundleAsync();
            return ParseTrustedBundleCerts(response);
        }

        public static (X509Certificate2 ServerCertificate, IEnumerable<X509Certificate2> CertificateChain) GetServerCertificateAndChainFromFile(string serverWithChainFilePath, string serverPrivateKeyFilePath, ILogger logger = null)
        {
            string cert, privateKey;

            if (string.IsNullOrWhiteSpace(serverWithChainFilePath) || !File.Exists(serverWithChainFilePath))
            {
                throw new ArgumentException($"'{serverWithChainFilePath}' is not a path to a server certificate file");
            }

            if (string.IsNullOrWhiteSpace(serverPrivateKeyFilePath) || !File.Exists(serverPrivateKeyFilePath))
            {
                throw new ArgumentException($"'{serverPrivateKeyFilePath}' is not a path to a private key file");
            }

            using (var sr = new StreamReader(serverWithChainFilePath))
            {
                cert = sr.ReadToEnd();
            }

            using (var sr = new StreamReader(serverPrivateKeyFilePath))
            {
                privateKey = sr.ReadToEnd();
            }

            return ParseCertificateAndKey(cert, privateKey, logger);
        }

        public static IEnumerable<X509Certificate2> GetServerCACertificatesFromFile(string chainPath)
        {
            IEnumerable<X509Certificate2> certChain = !string.IsNullOrWhiteSpace(chainPath) ? ExtractCertsFromPem(chainPath) : null;
            return certChain;
        }

        public static IList<string> ParsePemCerts(string pemCerts)
        {
            if (string.IsNullOrEmpty(pemCerts))
            {
                throw new InvalidOperationException("Trusted certificates can not be null or empty.");
            }

            // Extract each certificate's string. The final string from the split will either be empty
            // or a non-certificate entry, so it is dropped.
            string delimiter = "-----END CERTIFICATE-----";
            string[] rawCerts = pemCerts.Split(new[] { delimiter }, StringSplitOptions.None);
            return rawCerts
                .Take(rawCerts.Count() - 1) // Drop the invalid entry
                .Select(c => $"{c}{delimiter}")
                .ToList(); // Re-add the certificate end-marker which was removed by split
        }

        public static IEnumerable<X509Certificate2> ParseTrustedBundleFromFile(string trustBundleFilePath)
        {
            string certs;

            if (string.IsNullOrWhiteSpace(trustBundleFilePath) || !File.Exists(trustBundleFilePath))
            {
                throw new ArgumentException($"'{trustBundleFilePath}' is not a path to a trust bundle certificates file");
            }

            using (var sr = new StreamReader(trustBundleFilePath))
            {
                certs = sr.ReadToEnd();
            }

            return ParseTrustedBundleCerts(certs);
        }

        internal static IEnumerable<X509Certificate2> ParseTrustedBundleCerts(string trustedCACerts)
        {
            Preconditions.CheckNotNull(trustedCACerts, nameof(trustedCACerts));
            return GetCertificatesFromPem(ParsePemCerts(trustedCACerts));
        }

        internal static (X509Certificate2, IEnumerable<X509Certificate2>) ParseCertificateResponse(ServerCertificateResponse response, ILogger logger = null) =>
            ParseCertificateAndKey(response.Certificate, response.PrivateKey, logger);

        internal static (X509Certificate2, IEnumerable<X509Certificate2>) ParseCertificateAndKey(string certificateWithChain, string privateKey, ILogger logger = null)
        {
            IEnumerable<string> pemCerts = ParsePemCerts(certificateWithChain);

            if (pemCerts.FirstOrDefault() == null)
            {
                throw new InvalidOperationException("Certificate is required");
            }

            IEnumerable<X509Certificate2> certsChain = GetCertificatesFromPem(pemCerts.Skip(1));

            var certWithNoKey = new X509Certificate2(Encoding.UTF8.GetBytes(pemCerts.First()));
            var certWithPrivateKey = AttachPrivateKey(certWithNoKey, privateKey, logger);

            return (certWithPrivateKey, certsChain);
        }

        static string ToHexString(byte[] bytes)
        {
            Preconditions.CheckNotNull(bytes);
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }

        static Option<string> GetCommonNameFromSubject(string subject)
        {
            Option<string> commonName = Option.None<string>();
            string[] parts = subject.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                string partTrimed = part.Trim();
                if (partTrimed.StartsWith("CN", StringComparison.OrdinalIgnoreCase))
                {
                    string[] cnParts = partTrimed.Split(new char[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                    if (cnParts.Length > 1)
                    {
                        commonName = Option.Some(cnParts[1].Trim());
                    }
                }
            }

            return commonName;
        }

        static X509Certificate2 AttachPrivateKey(X509Certificate2 certificate, string pemEncodedKey, ILogger logger)
        {
            var retryCount = 0;

            while (retryCount++ < MaxCertImportRetryCount)
            {
                var pkcs8Label = "PRIVATE KEY";
                var rsaLabel = "RSA PRIVATE KEY";
                var ecLabel = "EC PRIVATE KEY";
                var keyAlgorithm = certificate.GetKeyAlgorithm();
                var isPkcs8 = pemEncodedKey.IndexOf(Header(pkcs8Label)) >= 0;

                X509Certificate2 result = null;

                try
                {
                    if (oidRsaEncryption.Value == keyAlgorithm)
                    {
                        logger?.LogDebug("Importing RSA private key");

                        var decodedKey = UnwrapPrivateKey(pemEncodedKey, isPkcs8 ? pkcs8Label : rsaLabel);
                        var key = RSA.Create();

                        if (isPkcs8)
                        {
                            key.ImportPkcs8PrivateKey(decodedKey, out _);
                        }
                        else
                        {
                            key.ImportRSAPrivateKey(decodedKey, out _);
                        }

                        result = certificate.CopyWithPrivateKey(key);

                        logger?.LogDebug("RSA private key has been imported and assigned to certificate");
                    }
                    else if (oidEcPublicKey.Value == keyAlgorithm)
                    {
                        logger?.LogDebug("Importing ECC private key");

                        var decodedKey = UnwrapPrivateKey(pemEncodedKey, isPkcs8 ? pkcs8Label : ecLabel);
                        var key = ECDsa.Create();

                        if (isPkcs8)
                        {
                            key.ImportPkcs8PrivateKey(decodedKey, out _);
                        }
                        else
                        {
                            key.ImportECPrivateKey(decodedKey, out _);
                        }

                        result = certificate.CopyWithPrivateKey(key);

                        logger?.LogDebug("ECC private key has been imported and assigned to certificate");
                    }
                }
                catch (Exception ex)
                {
                    var errorMessage = "Cannot import private key";
                    logger?.LogError(ex, errorMessage);
                    throw new InvalidOperationException(errorMessage, ex);
                }

                if (result == null)
                {
                    var errorMessage = $"Cannot use certificate, not supported key algorithm: ${keyAlgorithm}";
                    logger?.LogError(errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }

                var needsRetry = false;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // On Windows the certificate in 'result' gives an error when used with kestrel: "No credentials are available in the security"
                    // This is a suggested workaround that seems working (https://github.com/dotnet/runtime/issues/45680)
                    result = new X509Certificate2(result.Export(X509ContentType.Pkcs12));

                    // On Windows the imported certificate sometimes fails to use the private key and kestrel fails accepting connections (this is not related
                    // to the other problem above). Try to access the private key and catch the error early. If it fails, re-importing the certificate
                    // solves the problem:
                    try
                    {
                        if (oidRsaEncryption.Value == keyAlgorithm)
                        {
                            _ = result.GetRSAPrivateKey();
                        }
                        else
                        {
                            _ = result.GetECDsaPrivateKey();
                        }
                    }
                    catch
                    {
                        needsRetry = true;
                    }
                }

                if (!needsRetry)
                {
                    return result;
                }

                logger?.LogWarning("Error importing certificate, retrying");
                Thread.Sleep(TimeSpan.FromSeconds(1)); // Slow down retries a bit
            }

            throw new InvalidOperationException("Cannot import server certificate, giving up");
        }

        static byte[] UnwrapPrivateKey(string pemEncodedKey, string algoLabel)
        {
            var headerIndex = pemEncodedKey.IndexOf(Header(algoLabel));
            var footerIndex = pemEncodedKey.IndexOf(Footer(algoLabel));

            if (headerIndex < 0 || footerIndex < 0)
            {
                throw new InvalidOperationException($"Certificate key algorithm indicates {algoLabel}, but cannot unwrap key - headers not found");
            }

            byte[] decodedKey;

            try
            {
                var dataIndex = headerIndex + Header(algoLabel).Length;
                decodedKey = Convert.FromBase64String(pemEncodedKey.Substring(dataIndex, footerIndex - dataIndex));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Cannot decode private key: base64 decoding failed after removing headers", ex);
            }

            return decodedKey;
        }

        static string Header(string label) => $"-----BEGIN {label}-----";
        static string Footer(string label) => $"-----END {label}-----";
    }
}
