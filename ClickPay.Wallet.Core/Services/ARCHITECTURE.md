# Wallet Core Provider Architecture

## Purpose
This document guides agents and developers on the provider-based architecture currently adopted in `ClickPay.Wallet.Core`.

The core exposes only blockchain-neutral services. All network-specific logic is encapsulated in internal providers, so the public surface remains stable and easily reusable by MAUI, Blazor, simulators, and future hosts.

## Main Components
- **MultiChainWalletService** – neutral entry point consumed by the UIs. Retrieves the vault via `WalletKeyService`, resolves the requested asset in the `WalletProviderRegistry`, and forwards operations (overview, receive, transactions, send) to the corresponding provider.
- **WalletProviderRegistry** – maintains the map `BlockchainNetwork → IWalletProvider`, enforces provider uniqueness, and offers:
  - `GetRegisteredNetworks` to list available networks for the UIs;
  - `Resolve(CryptoAsset)` (internal) to select the provider during core operations.
- **IWalletProvider** – public interface defining the required operations for any supported network. Concrete implementations are internal.
- **WalletSendRequest** – public record representing a fund transfer. Used in the contract between `MultiChainWalletService` and providers.
- **WalletMnemonicService** – provides mnemonic generation and validation. UIs use it during onboarding.

## Blockchain-specific providers
Each network lives in `ClickPay.Wallet.Core/Blockchain/<Chain>/` and includes:
- a `*WalletService` that implements account derivation and network-specific logic (UTXO, RPC, transaction construction);
- any network clients (e.g., `BitcoinNetworkClient`);
- the `*WalletProvider` that implements `IWalletProvider` and translates neutral operations into calls to the internal service.

Classes are `internal` to prevent direct consumption by UIs. Access for tests and simulators is allowed via `InternalsVisibleTo` defined in `Properties/AssemblyInfo.cs`.

## Dependency registration
`WalletServiceCollectionExtensions.AddWalletCore` registers all required services:
1. neutral services (`WalletKeyService`, `WalletMnemonicService`, `WalletErrorLocalizer`);
2. specific providers and clients (Bitcoin, Solana, Ethereum) as scoped;
3. `WalletProviderRegistry` and `MultiChainWalletService`.

UIs call `builder.Services.AddWalletCore()` in their respective `Program.cs` to get the full configuration.

## Operational flow
1. The user interface calls a method on `MultiChainWalletService` passing the desired asset.
2. The service retrieves the vault and validates the presence of a mnemonic.
3. The `WalletProviderRegistry` resolves the provider compatible with the asset.
4. The provider executes the specific logic (RPC, PSBT construction, transaction mapping) and returns neutral DTOs (`WalletOverview`, `WalletTransaction`, `WalletSendResult`).
5. Any errors are converted into `WalletError`, providing localizable and consistent messages for all UIs.

## Extending the solution
To add a new network:
1. Create a folder under `Blockchain/<NewNetwork>/` with the `internal` implementations of the service and provider.
2. Implement `IWalletProvider` mapping the neutral operations to the network protocol.
3. Register the provider in `AddWalletCore` (`services.AddScoped<IWalletProvider, NewNetworkWalletProvider>();`).
4. Update the asset JSON and, if necessary, the simulations to include the new network.

## Service cache
- All services that query the network apply a file-based cache with a fixed expiration of 5 minutes (`ServiceCacheDefaults.Lifetime`).
- `MultiChainWalletService` always reads the data from the file (even if expired), immediately returns the value to the UI, and, when necessary, starts a background refresh that updates the file and raises the `OverviewRefreshed`/`TransactionsRefreshed` events.
- `CoinGeckoExchangeRateService` follows the same strategy: it returns the cached rate instantly and updates in the background, raising `RateRefreshed` when a new value is available.
- Consumers (Blazor, MAUI, simulators) subscribe to events to automatically update the UI when fresh data arrives, while still providing an immediate response thanks to the cached value.

## Conventions
- Providers must return amounts in decimal units (not in lamports/satoshis) and complete DTOs with user-readable symbols.
- Internal exceptions must be converted via `WalletError` to maintain uniform messages.
- UIs must never resolve specific providers from the container: they access only neutral services.

By following these rules, the architecture remains modular, testable, and secure.
