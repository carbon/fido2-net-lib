﻿using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Fido2NetLib.Objects;

namespace Fido2NetLib
{
    public static class CryptoUtils
    {
        public static HashAlgorithm GetHasher(HashAlgorithmName hashName)
        {
            switch (hashName.Name)
            {
                case "SHA1":
                    return SHA1.Create();
                case "SHA256":
                case "HS256":
                case "RS256":
                case "ES256":
                case "PS256":
                    return SHA256.Create();
                case "SHA384":
                case "HS384":
                case "RS384":
                case "ES384":
                case "PS384":
                    return SHA384.Create();
                case "SHA512":
                case "HS512":
                case "RS512":
                case "ES512":
                case "PS512":
                    return SHA512.Create();
                default:
                    throw new ArgumentOutOfRangeException(nameof(hashName));
            }
        }

        public static HashAlgorithmName HashAlgFromCOSEAlg(COSE.Algorithm alg)
        {
            return alg switch
            {
                COSE.Algorithm.RS1 => HashAlgorithmName.SHA1,
                COSE.Algorithm.ES256 => HashAlgorithmName.SHA256,
                COSE.Algorithm.ES384 => HashAlgorithmName.SHA384,
                COSE.Algorithm.ES512 => HashAlgorithmName.SHA512,
                COSE.Algorithm.PS256 => HashAlgorithmName.SHA256,
                COSE.Algorithm.PS384 => HashAlgorithmName.SHA384,
                COSE.Algorithm.PS512 => HashAlgorithmName.SHA512,
                COSE.Algorithm.RS256 => HashAlgorithmName.SHA256,
                COSE.Algorithm.RS384 => HashAlgorithmName.SHA384,
                COSE.Algorithm.RS512 => HashAlgorithmName.SHA512,
                COSE.Algorithm.ES256K => HashAlgorithmName.SHA256,
                (COSE.Algorithm)4 => HashAlgorithmName.SHA1,
                (COSE.Algorithm)11 => HashAlgorithmName.SHA256,
                (COSE.Algorithm)12 => HashAlgorithmName.SHA384,
                (COSE.Algorithm)13 => HashAlgorithmName.SHA512,
                COSE.Algorithm.EdDSA => HashAlgorithmName.SHA512,
                _ => throw new Fido2VerificationException("Unrecognized COSE alg value"),
            };
        }

        public static bool ValidateTrustChain(X509Certificate2[] trustPath, X509Certificate2[] attestationRootCertificates)
        {
            foreach (var attestationRootCert in attestationRootCertificates)
            {
                var chain = new X509Chain();
                chain.ChainPolicy.ExtraStore.Add(attestationRootCert);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                if (trustPath.Length > 1)
                {
                    foreach (var cert in trustPath.Skip(1).Reverse())
                    {
                        chain.ChainPolicy.ExtraStore.Add(cert);
                    }
                }
                bool valid = chain.Build(trustPath[0]);

                // because we are using AllowUnknownCertificateAuthority we have to verify that the root matches ourselves
                var chainRoot = chain.ChainElements[^1].Certificate;
                valid = valid && chainRoot.RawData.SequenceEqual(attestationRootCert.RawData);

                if (valid)
                    return true;
            }
            return false;
        }

        public static byte[] SigFromEcDsaSig(byte[] ecDsaSig, int keySize)
        {
            var decoded = Asn1Element.Decode(ecDsaSig);
            var r = decoded[0].GetIntegerBytes();
            var s = decoded[1].GetIntegerBytes();

            // .NET requires IEEE P-1363 fixed size unsigned big endian values for R and S
            // ASN.1 requires storing positive integer values with any leading 0s removed
            // Convert ASN.1 format to IEEE P-1363 format 
            // determine coefficient size 

            // common coefficient sizes include: 32, 48, and 64
            var coefficientSize = (int)Math.Ceiling((decimal)keySize / 8);

            // Create buffer to copy R into 
            Span<byte> p1363R = coefficientSize <= 64
                ? stackalloc byte[coefficientSize]
                : new byte[coefficientSize];

            if (0x0 == r[0] && (r[1] & (1 << 7)) != 0)
            {
                r.Slice(1).CopyTo(p1363R.Slice(coefficientSize - r.Length + 1));
            }
            else
            {
                r.CopyTo(p1363R.Slice(coefficientSize - r.Length));
            }

            // Create byte array to copy S into 
            Span<byte> p1363S = coefficientSize <= 64
                ? stackalloc byte[coefficientSize]
                : new byte[coefficientSize];

            if (0x0 == s[0] && (s[1] & (1 << 7)) != 0)
            {
                s.Slice(1).CopyTo(p1363S.Slice(coefficientSize - s.Length + 1));
            }
            else
            {
                s.CopyTo(p1363S.Slice(coefficientSize - s.Length));
            }

            // Concatenate R + S coordinates and return the raw signature
            var concated = new byte[p1363R.Length + p1363S.Length];

            p1363R.CopyTo(concated);
            p1363S.CopyTo(concated.AsSpan(p1363R.Length));

            return concated;
        }

        /// <summary>
        /// Convert PEM formated string into byte array.
        /// </summary>
        /// <param name="pemStr">source string.</param>
        /// <returns>output byte array.</returns>
        public static byte[] PemToBytes(ReadOnlySpan<char> pemStr)
        {
            var range = PemEncoding.Find(pemStr);

            byte[] data = new byte[range.DecodedDataLength];

            Convert.TryFromBase64Chars(pemStr[range.Base64Data], data, out _);

            return data;
        }

        public static string CDPFromCertificateExts(X509ExtensionCollection exts)
        {
            var cdp = "";
            foreach (var ext in exts)
            {
                if (ext.Oid.Value is "2.5.29.31") // id-ce-CRLDistributionPoints
                {
                    var asnData = Asn1Element.Decode(ext.RawData);

                    var el = asnData[0][0][0][0];

                    cdp = Encoding.ASCII.GetString(el.GetOctetString(el.Tag));
                }
            }
            return cdp;
        }

        public static bool IsCertInCRL(byte[] crl, X509Certificate2 cert)
        {
            var asnData = Asn1Element.Decode(crl);

            if (7 > asnData[0].Sequence.Count)
                return false; // empty CRL

            // Certificate users MUST be able to handle serialNumber values up to 20 octets.

            var revokedAsnSequence = asnData[0][5].Sequence;
            var revokedSerialNumbers = new byte[revokedAsnSequence.Count][];

            for (int i = 0; i < revokedAsnSequence.Count; i++)
            {
                revokedSerialNumbers[i] = revokedAsnSequence[i][0].GetIntegerBytes().ToArray();
            }

            var certificateSerialNumber = cert.GetSerialNumber().ToArray(); // defensively copy

            Array.Reverse(certificateSerialNumber); // convert to big-endian order

            foreach (var revokedSerialNumber in revokedSerialNumbers)
            {
                if (revokedSerialNumber.AsSpan().SequenceEqual(certificateSerialNumber))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
