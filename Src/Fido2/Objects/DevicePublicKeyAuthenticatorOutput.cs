﻿#nullable disable
namespace Fido2NetLib.Objects;

using System;
using Fido2NetLib.Cbor;
using NSec.Cryptography;


public sealed class DevicePublicKeyAuthenticatorOutput
{
    // https://w3c.github.io/webauthn/#sctn-device-publickey-attestation-calculations
    internal static ReadOnlySpan<byte> _dpkAuthDataPrefix => new byte[] {
        0x64, 0x65, 0x76, 0x69, 0x63, 0x65, 0x20, 0x62, 0x6f, 0x75, 0x6e, 0x64, 0x20,
        0x6b, 0x65, 0x79, 0x20, 0x61, 0x74, 0x74, 0x65, 0x73, 0x74, 0x61, 0x74, 0x69,
        0x6f, 0x6e, 0x20, 0x73, 0x69, 0x67, 0x00, 0xff, 0xff, 0xff, 0xff };

    internal CborMap _attObj;

    public byte[] AuthData => DataHelper.Concat(_dpkAuthDataPrefix, AaGuid.ToByteArray());

    public byte[] Hash => DataHelper.Concat(DevicePublicKey.GetBytes(), Nonce);

    public byte[] GetBytes() => _attObj.Encode();

    public byte[] AuthenticationMatcher => DataHelper.Concat(AaGuid.ToByteArray(), DevicePublicKey.GetBytes());

    public ReadOnlySpan<byte> Nonce => _nonce;

    public DevicePublicKeyAuthenticatorOutput(CborMap attObjForDevicePublicKey)
    {
        AaGuid = new Guid((byte[])attObjForDevicePublicKey["aaguid"]);
        DevicePublicKey = new CredentialPublicKey((byte[])attObjForDevicePublicKey["dpk"]);
        Scope = (uint)attObjForDevicePublicKey["scope"];
        _nonce = (byte[])attObjForDevicePublicKey["nonce"];
        Fmt = (string)attObjForDevicePublicKey["fmt"];
        AttStmt = (CborMap)attObjForDevicePublicKey["attStmt"];
        EpAtt = false;
        if ((Fmt == "enterprise") && attObjForDevicePublicKey["epAtt"] is not null)
            EpAtt = (bool)attObjForDevicePublicKey["epAtt"];
        _attObj = attObjForDevicePublicKey;
    }

    public DevicePublicKeyAuthenticatorOutput(byte[] attObjForDevicePublicKey) : this((CborMap)CborObject.Decode(attObjForDevicePublicKey)) { }

    /// <summary>
    /// The AAGUID of the authenticator. Can be used to identify the make and model of the authenticator.
    /// <see cref="https://www.w3.org/TR/webauthn/#aaguid"/>
    /// </summary>
    public Guid AaGuid { get; }

    /// <summary>
    /// The credential public key encoded in COSE_Key format, as defined in 
    /// Section 7 of RFC8152, using the CTAP2 canonical CBOR encoding form.
    /// <see cref="https://www.w3.org/TR/webauthn/#credential-public-key"/>
    /// </summary>
    public CredentialPublicKey DevicePublicKey { get; }

    /// <summary>
    /// Whether this key is scoped to the entire device, or a loosely-defined, narrower scope called "app". 
    /// For example, a "device"-scoped key is expected to be the same between an app and a browser on the same device, while an "app"-scoped key would probably not be.
    /// Whatever the scope, a device key is still specific to a given credential and does not provide any ability to link credentials.
    /// Whether device-scoped or not, keys are still device-bound. I.e.an app-scoped key does not enjoy lesser protection from extraction.
    /// A value of 0x00 means "entire device" ("all apps") scope. 
    /// 0x01 means "per-app" scope. Values other than 0x00 or 0x01 are reserved for future use.
    /// </summary>
    public uint Scope { get; }

    /// <summary>
    /// An authenticator-generated random nonce for inclusion in the attestation signature.
    /// If the authenticator chooses to not generate a nonce, it sets this to a zero-length byte string. 
    /// See the note below about "randomNonce" for a discussion on the nonce's purpose.
    /// </summary>
    public byte[] _nonce { get; }

    /// <summary>
    /// Attestation statement formats are identified by a string, called an attestation statement format identifier, chosen by the author of the attestation statement format.
    /// <see cref="https://w3c.github.io/webauthn/#sctn-attstn-fmt-ids"/>
    /// </summary>
    public string Fmt { get; }

    /// <summary>
    /// A CborMap encoded attestation statement.
    /// </summary>
    public CborMap AttStmt { get; }

    /// <summary>
    /// An optional boolean that indicates whether the attestation statement contains uniquely identifying information.
    /// This can only be true when the `attestation` field of the extension input is "enterprise" and either the user-agent or the authenticator permits uniquely identifying attestation for the requested RP ID.
    /// </summary>
    public bool? EpAtt { get; }
}
