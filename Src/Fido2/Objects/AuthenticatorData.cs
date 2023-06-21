﻿#nullable disable

using System;
using System.Buffers;

using Fido2NetLib.Cbor;
using Fido2NetLib.Exceptions;

namespace Fido2NetLib.Objects;

public sealed class AuthenticatorData
{
    /// <summary>
    /// Minimum length of the authenticator data structure.
    /// <see cref="https://www.w3.org/TR/webauthn/#sec-authenticator-data"/>
    /// </summary>
    internal const int MinLength = SHA256HashLenBytes + sizeof(AuthenticatorFlags) + sizeof(uint);

    private const int SHA256HashLenBytes = 32; // 256 bits, 8 bits per byte

    /// <summary>
    /// SHA-256 hash of the RP ID the credential is scoped to.
    /// </summary>
    public byte[] RpIdHash;

    /// <summary>
    /// Flags contains information from the authenticator about the authentication 
    /// and whether or not certain data is present in the authenticator data.
    /// </summary>
    private readonly AuthenticatorFlags _flags;

    /// <summary>
    /// UserPresent indicates that the user presence test has completed successfully.
    /// <see cref="https://www.w3.org/TR/webauthn/#up"/>
    /// </summary>
    public bool UserPresent => _flags.HasFlag(AuthenticatorFlags.UP);

    /// <summary>
    /// UserVerified indicates that the user verification process has completed successfully.
    /// <see cref="https://www.w3.org/TR/webauthn/#uv"/>
    /// </summary>
    public bool UserVerified => _flags.HasFlag(AuthenticatorFlags.UV);

    /// <summary>
    /// A Public Key Credential Source's generating authenticator determines at creation time whether the public key credential source is allowed to be backed up. 
    /// Backup eligibility is signaled in authenticator data's flags along with the current backup state. 
    /// Backup eligibility is a credential property and is permanent for a given public key credential source. 
    /// A backup eligible public key credential source is referred to as a multi-device credential whereas one that is not backup eligible is referred to as a single-device credential.
    /// <see cref="https://w3c.github.io/webauthn/#backup-eligibility"/>
    /// </summary>
    public bool IsBackupEligible => _flags.HasFlag(AuthenticatorFlags.BE);

    /// <summary>
    /// The current backup state of a multi-device credential as determined by the current managing authenticator. 
    /// Backup state is signaled in authenticator data's flags and can change over time.
    /// <see cref="https://w3c.github.io/webauthn/#backup-state"/>
    /// </summary>
    public bool BackupState => _flags.HasFlag(AuthenticatorFlags.BS);

    /// <summary>
    /// HasAttestedCredentialData indicates that the authenticator added attested credential data to the authenticator data.
    /// <see cref="https://www.w3.org/TR/webauthn/#attested-credential-data"/>
    /// </summary>
    public bool HasAttestedCredentialData => _flags.HasFlag(AuthenticatorFlags.AT);

    /// <summary>
    /// HasExtensionsData indicates that the authenticator added extension data to the authenticator data.
    /// <see cref="https://www.w3.org/TR/webauthn/#authdataextensions"/>
    /// </summary>
    public bool HasExtensionsData => _flags.HasFlag(AuthenticatorFlags.ED);

    /// <summary>
    /// Signature counter, 32-bit unsigned big-endian integer. 
    /// </summary>
    public uint SignCount;

    /// <summary>
    /// Attested credential data is a variable-length byte array added to the 
    /// authenticator data when generating an attestation object for a given credential.
    /// </summary>
    public AttestedCredentialData AttestedCredentialData;

    /// <summary>
    /// Optional extensions to suit particular use cases.
    /// </summary>
    public Extensions Extensions;

    public AuthenticatorData(byte[] rpIdHash, AuthenticatorFlags flags, uint signCount, AttestedCredentialData acd, Extensions exts = null)
    {
        RpIdHash = rpIdHash;
        _flags = flags;
        SignCount = signCount;
        AttestedCredentialData = acd;
        Extensions = exts;
    }

    public AuthenticatorData(byte[] authData)
    {
        // Input validation
        if (authData is null)
            throw new Fido2VerificationException(Fido2ErrorCode.MissingAuthenticatorData, Fido2ErrorMessages.MissingAuthenticatorData);

        if (authData.Length < MinLength)
            throw new Fido2VerificationException(Fido2ErrorCode.InvalidAuthenticatorData, Fido2ErrorMessages.InvalidAuthenticatorData_TooShort);

        // Input parsing
        var reader = new MemoryReader(authData);

        RpIdHash = reader.ReadBytes(SHA256HashLenBytes);

        _flags = (AuthenticatorFlags)reader.ReadByte();

        SignCount = reader.ReadUInt32BigEndian();

        // Attested credential data is only present if the AT flag is set
        if (HasAttestedCredentialData)
        {
            // Decode attested credential data, which starts at the next byte past the minimum length of the structure
            AttestedCredentialData = new AttestedCredentialData(authData.AsMemory(reader.Position), out int bytesRead);

            reader.Advance(bytesRead);
        }

        // Extensions data is only present if the ED flag is set
        if (HasExtensionsData)
        {
            // Read the CBOR object
            var ext = CborObject.Decode(authData.AsMemory(reader.Position), out int bytesRead);

            reader.Advance(bytesRead);

            // Encode the CBOR object back to a byte array
            Extensions = new Extensions(ext.Encode());
        }

        // Ensure there are no remaining bytes left over after decoding the structure
        if (reader.RemainingBytes != 0)
            throw new Fido2VerificationException(Fido2ErrorCode.InvalidAuthenticatorData, "Leftover bytes decoding AuthenticatorData");            
    }

    public byte[] ToByteArray()
    {
        var writer = new ArrayBufferWriter<byte>(512);

        writer.Write(RpIdHash);

        writer.Write(stackalloc byte[1] { (byte)_flags });

        writer.WriteUInt32BigEndian(SignCount);

        if (HasAttestedCredentialData)
        {
            AttestedCredentialData.WriteTo(writer);
        }

        if (HasExtensionsData)
        {
            writer.Write(Extensions.GetBytes());
        }

        return writer.WrittenSpan.ToArray();
    }
}
