# Metadata-Driven Asset Configuration

This wallet is designed to stay neutral with respect to the supported digital assets. All literal identifiers such as asset symbols, human-readable names, networks, contract codes, and routing hints **must** be declared exclusively inside the JSON configuration files within this directory. C# source code must never embed those details directly.

By treating each asset definition as data, the application can onboard new cryptocurrencies or tokens simply by adding a new JSON document that follows the established schema. No recompilation or changes to the wallet logic are required—the UI, routing, and service layers automatically adapt based on the metadata provided here.

This approach keeps the codebase clean, minimizes the risk of hard-coded assumptions, and empowers both developers and AI agents to extend the wallet in a safe, declarative, and testable manner.