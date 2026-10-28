package ReviewCenter {
    part def AttestationKeyProvider {
        doc /* Chooses and loads the signing key of the attestation from the Attestation settings: a self-signed key that the server creates and keeps in a folder, or a certificate of a certificate store found by its thumbprint; describes the key and its status for the pages; applies the RequireTrustedKey policy; and warns, without refusing it for that reason, about a code-signing-only certificate that Adobe Acrobat will not trust for documents (the classification is part of the key description; chain and revocation are not checked by the server). The private key is never exported. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Evidence/AttestationKeyProviderFactory.cs, SelfSignedKeyProvider.cs, CertificateStoreKeyProvider.cs, AttestationKeyProviderBase.cs, AttestationKeyPolicy.cs, IAttestationKeyProvider.cs, AttestationSigningKey.cs, AttestationKeyInfo.cs, AttestationKeyException.cs, AttestationOptions.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Evidence/AttestationKeyPolicyTests.cs, SelfSignedKeyProviderTests.cs, CertificateStoreKeyProviderTests.cs */
        comment designRef /* Design: docs/design/review-center/evidence/attestation-key-provider.md */
        comment verificationRef /* Verification: docs/verification/review-center/evidence/attestation-key-provider.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/evidence/attestation-key-provider.yaml */
    }
}
