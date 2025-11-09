# Blockchain Provider Metadata

This directory contains configuration files that describe how the wallet interacts with each supported blockchain. The data is stored as JSON documents so new blockchains can be onboarded without recompiling the codebase.

Each configuration document exposes a list of **providers**. A provider maps one or more blockchain identifiers to the client library and handler class that implement the required functionality. Example properties:

- `library`: Logical name of the library (e.g., `NBitcoin`, `Nethereum`, `Solnet`).
- `package`: NuGet package providing the implementation (optional, for documentation/reference).
- `handler`: Fully qualified .NET type that encapsulates blockchain operations (optional, enables reflection-based wiring if needed).
- `blockchains`: Collection of normalized identifiers matching the `blockchain` property in the asset metadata under `CryptoAssets`.
- `metadata`: Arbitrary key/value pairs for additional hints (network URLs, feature flags, etc.).

All JSON files in this folder are scanned at runtime by `BlockchainProviderRegistry`. The registry validates for duplicate blockchain declarations and exposes a read-only view to the rest of the system.

## Extending Support

To add a new blockchain:

1. Create (or extend) a JSON file in this directory.
2. Register the relevant library/package/handler information.
3. Ensure the new blockchain identifier is referenced by asset metadata in `CryptoAssets`.

No source changes are required—the registry automatically incorporates the new definitions on startup.
