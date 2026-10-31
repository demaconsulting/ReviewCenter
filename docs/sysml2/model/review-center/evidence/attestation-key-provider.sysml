package ReviewCenter {
    part def AttestationKeyProvider {
        doc /* Chooses and loads the signing key of the attestation from the Attestation settings: a self-signed key that the server creates and keeps in a folder, protected at rest with the Data Protection key ring (a plaintext file of an earlier version is ignored with a warning, an expired key is archived and replaced, and a provider told not to create anything only describes), or a certificate of a certificate store found by its thumbprint, or a certificate with its private key in a PFX or PEM file (the password comes from a setting or a password file, the key stays in memory, and a key without a password is a warning or, with RequireEncryptedKeys, an error); describes the key and its status for the pages; applies the RequireTrustedKey policy; warns when a certificate of the store or file provider ends within ExpiryWarningDays or has ended; and warns, without refusing it for that reason, about a code-signing-only certificate that Adobe Acrobat will not trust for documents (the classification is part of the key description; chain and revocation are not checked by the server). The private key is never exported. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Evidence/AttestationKeyProviderFactory.cs, SelfSignedKeyProvider.cs, CertificateStoreKeyProvider.cs, FileKeyProvider.cs, AttestationKeyProviderBase.cs, AttestationKeyPolicy.cs, IAttestationKeyProvider.cs, AttestationSigningKey.cs, AttestationKeyInfo.cs, AttestationKeyException.cs, AttestationOptions.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Evidence/AttestationKeyPolicyTests.cs, SelfSignedKeyProviderTests.cs, CertificateStoreKeyProviderTests.cs, FileKeyProviderTests.cs */
        comment designRef /* Design: docs/design/review-center/evidence/attestation-key-provider.md */
        comment verificationRef /* Verification: docs/verification/review-center/evidence/attestation-key-provider.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/evidence/attestation-key-provider.yaml */
    }
}
