# ClickPay Wallet Core

Core library for multi-chain cryptocurrency wallet operations.

## Documentation Index

### 📋 Core Architecture
- **[ARCHITECTURE.md](./Services/ARCHITECTURE.md)** - Main architectural documentation
  - Provider-based architecture
  - Dependency injection setup
  - Service registration and operational flow
  - **NEW**: Asset file naming convention (`{CODE}-{BLOCKCHAIN}.json`)

### 🔄 Swap System
- **[SwapUtilityArchitecture.md](./Utility/SwapUtilityArchitecture.md)** - Swap system documentation
  - Multi-provider swap architecture
  - Quote generation and validation
  - Cross-chain swap support

### 🎨 Assets & Icons
- **[METADATA_NOTICE.md](./METADATA_NOTICE.md)** - Icon licensing and attribution
  - Cryptocurrency icon sources
  - Licensing information (CC0 1.0)
  - Icon addition guidelines

## Quick Start

### Asset Configuration
Assets are configured via JSON files following the naming convention:

```
{ASSET_CODE}-{BLOCKCHAIN}.json
```

**Examples:**
- `BTC-Bitcoin.json` - Bitcoin native asset
- `ETH-Ethereum.json` - Ethereum native asset
- `SOL-Solana.json` - Solana native asset  
- `XLM-Stellar.json` - Stellar native asset
- `EURC-Stellar.json` - EURC token on Stellar

### Adding New Assets
1. Create JSON file with proper naming convention
2. Define asset properties (code, network, contractAddress, etc.)
3. System automatically discovers and registers the asset
4. All wallet operations become available immediately

### Blockchain Support
- **Bitcoin**: UTXO-based transactions
- **Ethereum**: EVM-compatible chains
- **Solana**: High-speed transactions
- **Stellar**: Fast, low-cost payments with custom assets

## Development

See [ARCHITECTURE.md](./Services/ARCHITECTURE.md) for detailed development guidelines, including how to add new blockchain providers and extend the system.

---

*For cryptocurrency icon attribution, see [METADATA_NOTICE.md](./METADATA_NOTICE.md)*