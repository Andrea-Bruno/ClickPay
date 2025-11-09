# Wallet Core Provider Architecture

## Purpose
Questo documento orienta gli agenti e gli sviluppatori sull'architettura provider-based attualmente adottata in `ClickPay.Wallet.Core`.

Il core espone solo servizi neutri rispetto alla blockchain. Tutta la logica specifica di rete è incapsulata in provider interni, così la superficie pubblica rimane stabile e facilmente riutilizzabile da MAUI, Blazor, simulatori e futuri host.

## Componenti principali
- **MultiChainWalletService** – entry point neutro consumato dalle UI. Recupera il vault tramite `WalletKeyService`, risolve l'asset richiesto nel `WalletProviderRegistry` e inoltra le operazioni (overview, receive, transactions, send) al provider corrispondente.
- **WalletProviderRegistry** – mantiene la mappa `BlockchainNetwork → IWalletProvider`, impone l'unicità dei provider e offre:
  - `GetRegisteredNetworks` per elencare le reti disponibili alle UI;
  - `Resolve(CryptoAsset)` (internal) per selezionare il provider durante le operazioni core.
- **IWalletProvider** – interfaccia pubblica che definisce le operazioni necessarie a qualsiasi rete supportata. Le implementazioni concrete sono internal.
- **WalletSendRequest** – record pubblico che rappresenta l'invio di fondi. Usato nel contratto tra `MultiChainWalletService` e i provider.
- **WalletMnemonicService** – fornisce generazione e validazione delle mnemonic. Le UI la usano durante l'onboarding.

## Provider specifici di blockchain
Ogni rete vive in `ClickPay.Wallet.Core/Blockchain/<Chain>/` e include:
- un servizio `*WalletService` che implementa la derivazione di account e la logica specifica (UTXO, RPC, costruzione transazioni);
- eventuali client di rete (es. `BitcoinNetworkClient`);
- il provider `*WalletProvider` che implementa `IWalletProvider` e traduce le operazioni neutre in chiamate al servizio interno.

Le classi sono `internal` per impedire il consumo diretto dalle UI. L'accesso per test e simulatori avviene con `InternalsVisibleTo` definito in `Properties/AssemblyInfo.cs`.

## Registrazione delle dipendenze
`WalletServiceCollectionExtensions.AddWalletCore` registra tutti i servizi necessari:
1. servizi neutri (`WalletKeyService`, `WalletMnemonicService`, `WalletErrorLocalizer`);
2. provider e client specifici (Bitcoin, Solana, Ethereum) come scoped;
3. `WalletProviderRegistry` e `MultiChainWalletService`.

Le UI chiamano `builder.Services.AddWalletCore()` nei rispettivi `Program.cs` per ottenere tutta la configurazione.

## Flusso operativo
1. L'interfaccia utente richiama un metodo su `MultiChainWalletService` passando l'asset desiderato.
2. Il servizio recupera il vault e convalida la presenza di una mnemonic.
3. Il `WalletProviderRegistry` risolve il provider compatibile con l'asset.
4. Il provider esegue la logica specifica (RPC, costruzione PSBT, mapping transazioni) e restituisce DTO neutri (`WalletOverview`, `WalletTransaction`, `WalletSendResult`).
5. Eventuali errori sono convertiti in `WalletError`, messaggi localizzabili e coerenti per tutte le UI.

## Estendere la soluzione
Per aggiungere una nuova rete:
1. Creare una cartella sotto `Blockchain/<NuovaRete>/` con le implementazioni `internal` del servizio e del provider.
2. Implementare `IWalletProvider` mappando le operazioni neutre sul protocollo della rete.
3. Registrare il provider in `AddWalletCore` (`services.AddScoped<IWalletProvider, NuovaReteWalletProvider>();`).
4. Aggiornare gli asset JSON e, se necessario, le simulazioni per includere la nuova rete.

## Cache dei servizi
- Tutti i servizi che interrogano la rete applicano una cache file-based con scadenza fissa di 5 minuti (`ServiceCacheDefaults.Lifetime`).
- `MultiChainWalletService` legge sempre il dato dal file (anche se scaduto), restituisce subito il valore alla UI e, quando necessario, avvia un refresh in background che aggiorna il file e solleva gli eventi `OverviewRefreshed`/`TransactionsRefreshed`.
- `CoinGeckoExchangeRateService` segue la stessa strategia: restituisce il rate cached all'istante e aggiorna in background emettendo `RateRefreshed` quando un nuovo valore è disponibile.
- I consumer (Blazor, MAUI, simulatori) si iscrivono agli eventi per aggiornare automaticamente la UI quando arrivano i dati freschi, mantenendo comunque una risposta immediata grazie al valore cache.

## Convenzioni
- I provider devono restituire importi in unità decimali (non in lamport/satoshi) e completare i DTO con simboli leggibili dall'utente.
- Le eccezioni interne devono essere convertite tramite `WalletError` per mantenere messaggi uniformi.
- Le UI non devono mai risolvere provider specifici dal contenitore: accedono solo a servizi neutri.

Seguendo queste regole l'architettura rimane modulare, testabile e sicura.
