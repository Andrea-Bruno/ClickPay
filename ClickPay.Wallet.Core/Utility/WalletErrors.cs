using System;
using System.Collections.Generic;
using System.Globalization;

namespace ClickPay.Wallet.Core.Utility
{
    public enum WalletErrorCode
    {
        None = 0,
        MnemonicMissing,
        AccountIndexOutOfRange,
        AddressIndexOutOfRange,
        DestinationMissing,
        AmountInvalid,
        InvalidAddress,
        VaultUnavailable,
        AssetNotSupported,
        NetworkUnavailable,
        Timeout,
        RpcError,
        SecureStorageUnavailable,
        OperationFailed,
        UnsupportedFeature
    }

    public class WalletOperationException : Exception
    {
        public WalletOperationException(WalletErrorCode code, string? message = null, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
        }

        public WalletErrorCode Code { get; }
    }

    public sealed class WalletServiceException : Exception
    {
        public WalletServiceException(WalletErrorCode code, string? message, Exception? innerException = null)
            : base(message ?? code.ToString(), innerException)
        {
            Code = code;
        }

        public WalletErrorCode Code { get; }
    }

    public static class WalletError
    {
        public static WalletOperationException MnemonicMissing(string? message = null) =>
            new WalletOperationException(WalletErrorCode.MnemonicMissing, message ?? "Mnemonic missing.");

        public static WalletOperationException AccountIndexOutOfRange(string? message = null) =>
            new WalletOperationException(WalletErrorCode.AccountIndexOutOfRange, message ?? "Account index out of range.");

        public static WalletOperationException AddressIndexOutOfRange(string? message = null) =>
            new WalletOperationException(WalletErrorCode.AddressIndexOutOfRange, message ?? "Address index out of range.");

        public static WalletOperationException DestinationMissing(string? message = null) =>
            new WalletOperationException(WalletErrorCode.DestinationMissing, message ?? "Destination address is required.");

        public static WalletOperationException AmountInvalid(string? message = null) =>
            new WalletOperationException(WalletErrorCode.AmountInvalid, message ?? "Amount must be positive.");

        public static WalletOperationException InvalidAddress(string? message = null, Exception? innerException = null) =>
            new WalletOperationException(WalletErrorCode.InvalidAddress, message ?? "Address is not valid.", innerException);

        public static WalletOperationException VaultUnavailable(string? message = null, Exception? innerException = null) =>
            new WalletOperationException(WalletErrorCode.VaultUnavailable, message ?? "Wallet vault is unavailable.", innerException);

        public static WalletOperationException AssetNotSupported(string? message = null) =>
            new WalletOperationException(WalletErrorCode.AssetNotSupported, message ?? "Asset is not supported.");

        public static WalletOperationException NetworkUnavailable(string? message = null, Exception? innerException = null) =>
            new WalletOperationException(WalletErrorCode.NetworkUnavailable, message ?? "Network is unavailable.", innerException);

        public static WalletOperationException Timeout(string? message = null, Exception? innerException = null) =>
            new WalletOperationException(WalletErrorCode.Timeout, message ?? "Operation timed out.", innerException);

        public static WalletOperationException RpcError(string? message = null, Exception? innerException = null) =>
            new WalletOperationException(WalletErrorCode.RpcError, message ?? "Blockchain RPC error.", innerException);

        public static WalletOperationException SecureStorageUnavailable(string? message = null, Exception? innerException = null) =>
            new WalletOperationException(WalletErrorCode.SecureStorageUnavailable, message ?? "Secure storage is unavailable.", innerException);

        public static WalletOperationException OperationFailed(string? message = null, Exception? innerException = null) =>
            new WalletOperationException(WalletErrorCode.OperationFailed, message ?? "The operation could not be completed.", innerException);

        public static WalletOperationException UnsupportedFeature(string? message = null) =>
            new WalletOperationException(WalletErrorCode.UnsupportedFeature, message ?? "This feature is not supported for the selected asset.");
    }

    public sealed class WalletErrorLocalizer
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<WalletErrorCode, string>> _catalog;

        public WalletErrorLocalizer()
        {
            _catalog = BuildCatalog();
        }

        public string GetMessage(WalletErrorCode code, CultureInfo? culture = null)
        {
            if (code == WalletErrorCode.None)
            {
                return string.Empty;
            }

            var language = Normalize(culture?.Name ?? CultureInfo.CurrentUICulture.Name);
            if (!_catalog.TryGetValue(language, out var catalog) && !_catalog.TryGetValue("en", out catalog))
            {
                return code.ToString();
            }

            if (catalog.TryGetValue(code, out var value))
            {
                return value;
            }

            if (_catalog.TryGetValue("en", out var fallback) && fallback.TryGetValue(code, out var fallbackValue))
            {
                return fallbackValue;
            }

            return code.ToString();
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "en";
            }

            var normalized = value.Trim().ToLowerInvariant();
            var dash = normalized.IndexOf('-');
            return dash >= 0 ? normalized[..dash] : normalized;
        }

        private static IReadOnlyDictionary<string, IReadOnlyDictionary<WalletErrorCode, string>> BuildCatalog()
        {
            return new Dictionary<string, IReadOnlyDictionary<WalletErrorCode, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new Dictionary<WalletErrorCode, string>
                {
                    [WalletErrorCode.MnemonicMissing] = "Wallet mnemonic is required.",
                    [WalletErrorCode.AccountIndexOutOfRange] = "Account index is out of range.",
                    [WalletErrorCode.AddressIndexOutOfRange] = "Address index is out of range.",
                    [WalletErrorCode.DestinationMissing] = "Destination address is required.",
                    [WalletErrorCode.AmountInvalid] = "Amount must be greater than zero.",
                    [WalletErrorCode.InvalidAddress] = "The destination address is not valid for this network.",
                    [WalletErrorCode.VaultUnavailable] = "No wallet has been configured on this device.",
                    [WalletErrorCode.AssetNotSupported] = "The selected asset is not supported yet.",
                    [WalletErrorCode.NetworkUnavailable] = "The blockchain network is currently unavailable.",
                    [WalletErrorCode.Timeout] = "The operation timed out. Please try again.",
                    [WalletErrorCode.RpcError] = "The blockchain node returned an error.",
                    [WalletErrorCode.SecureStorageUnavailable] = "Secure storage is not available in this environment.",
                    [WalletErrorCode.OperationFailed] = "The wallet operation could not be completed.",
                    [WalletErrorCode.UnsupportedFeature] = "This feature is not supported for the selected asset."
                },
                ["it"] = new Dictionary<WalletErrorCode, string>
                {
                    [WalletErrorCode.MnemonicMissing] = "Wallet passphrase is required.",
                    [WalletErrorCode.AccountIndexOutOfRange] = "Account index is out of range.",
                    [WalletErrorCode.AddressIndexOutOfRange] = "Address index is out of range.",
                    [WalletErrorCode.DestinationMissing] = "Destination address is required.",
                    [WalletErrorCode.AmountInvalid] = "Amount must be greater than zero.",
                    [WalletErrorCode.InvalidAddress] = "Destination address is not valid for this network.",
                    [WalletErrorCode.VaultUnavailable] = "No wallet has been configured on this device.",
                    [WalletErrorCode.AssetNotSupported] = "The selected asset is not yet supported.",
                    [WalletErrorCode.NetworkUnavailable] = "The blockchain network is currently unavailable.",
                    [WalletErrorCode.Timeout] = "The operation timed out. Please try again.",
                    [WalletErrorCode.RpcError] = "The blockchain node returned an error.",
                    [WalletErrorCode.SecureStorageUnavailable] = "Secure storage is not available in this environment.",
                    [WalletErrorCode.OperationFailed] = "Unable to complete the wallet operation.",
                    [WalletErrorCode.UnsupportedFeature] = "This feature is not supported for the selected asset."
                },
                ["fr"] = new Dictionary<WalletErrorCode, string>
                {
                    [WalletErrorCode.MnemonicMissing] = "La phrase secrète du portefeuille est requise.",
                    [WalletErrorCode.AccountIndexOutOfRange] = "L'index du compte est hors plage.",
                    [WalletErrorCode.AddressIndexOutOfRange] = "L'index de l'adresse est hors plage.",
                    [WalletErrorCode.DestinationMissing] = "L'adresse de destination est obligatoire.",
                    [WalletErrorCode.AmountInvalid] = "Le montant doit être supérieur à zéro.",
                    [WalletErrorCode.InvalidAddress] = "L'adresse de destination n'est pas valide pour ce réseau.",
                    [WalletErrorCode.VaultUnavailable] = "Aucun portefeuille n'est configuré sur cet appareil.",
                    [WalletErrorCode.AssetNotSupported] = "L'actif sélectionné n'est pas encore pris en charge.",
                    [WalletErrorCode.NetworkUnavailable] = "Le réseau blockchain est actuellement indisponible.",
                    [WalletErrorCode.Timeout] = "L'opération a expiré. Veuillez réessayer.",
                    [WalletErrorCode.RpcError] = "Le nœud blockchain a renvoyé une erreur.",
                    [WalletErrorCode.SecureStorageUnavailable] = "Le stockage sécurisé n'est pas disponible dans cet environnement.",
                    [WalletErrorCode.OperationFailed] = "L'opération sur le portefeuille n'a pas pu être effectuée.",
                    [WalletErrorCode.UnsupportedFeature] = "Cette fonctionnalité n'est pas prise en charge pour l'actif sélectionné."
                },
                ["es"] = new Dictionary<WalletErrorCode, string>
                {
                    [WalletErrorCode.MnemonicMissing] = "La frase secreta del monedero es obligatoria.",
                    [WalletErrorCode.AccountIndexOutOfRange] = "El índice de la cuenta está fuera de rango.",
                    [WalletErrorCode.AddressIndexOutOfRange] = "El índice de la dirección está fuera de rango.",
                    [WalletErrorCode.DestinationMissing] = "La dirección de destino es obligatoria.",
                    [WalletErrorCode.AmountInvalid] = "El importe debe ser mayor que cero.",
                    [WalletErrorCode.InvalidAddress] = "La dirección de destino no es válida para esta red.",
                    [WalletErrorCode.VaultUnavailable] = "No se ha configurado ningún monedero en este dispositivo.",
                    [WalletErrorCode.AssetNotSupported] = "El activo seleccionado aún no está soportado.",
                    [WalletErrorCode.NetworkUnavailable] = "La red blockchain no está disponible en este momento.",
                    [WalletErrorCode.Timeout] = "La operación ha caducado. Inténtalo de nuevo.",
                    [WalletErrorCode.RpcError] = "El nodo blockchain devolvió un error.",
                    [WalletErrorCode.SecureStorageUnavailable] = "El almacenamiento seguro no está disponible en este entorno.",
                    [WalletErrorCode.OperationFailed] = "No se pudo completar la operación del monedero.",
                    [WalletErrorCode.UnsupportedFeature] = "Esta función no está soportada para el activo seleccionado."
                },
                ["de"] = new Dictionary<WalletErrorCode, string>
                {
                    [WalletErrorCode.MnemonicMissing] = "Die Wallet-Passphrase ist erforderlich.",
                    [WalletErrorCode.AccountIndexOutOfRange] = "Der Kontenindex liegt außerhalb des gültigen Bereichs.",
                    [WalletErrorCode.AddressIndexOutOfRange] = "Der Adressindex liegt außerhalb des gültigen Bereichs.",
                    [WalletErrorCode.DestinationMissing] = "Die Zieladresse ist erforderlich.",
                    [WalletErrorCode.AmountInvalid] = "Der Betrag muss größer als Null sein.",
                    [WalletErrorCode.InvalidAddress] = "Die Zieladresse ist für dieses Netzwerk nicht gültig.",
                    [WalletErrorCode.VaultUnavailable] = "Auf diesem Gerät ist kein Wallet konfiguriert.",
                    [WalletErrorCode.AssetNotSupported] = "Das ausgewählte Asset wird noch nicht unterstützt.",
                    [WalletErrorCode.NetworkUnavailable] = "Das Blockchain-Netzwerk ist derzeit nicht verfügbar.",
                    [WalletErrorCode.Timeout] = "Die Operation ist abgelaufen. Bitte erneut versuchen.",
                    [WalletErrorCode.RpcError] = "Der Blockchain-Knoten hat einen Fehler gemeldet.",
                    [WalletErrorCode.SecureStorageUnavailable] = "Der sichere Speicher ist in dieser Umgebung nicht verfügbar.",
                    [WalletErrorCode.OperationFailed] = "Die Wallet-Operation konnte nicht abgeschlossen werden.",
                    [WalletErrorCode.UnsupportedFeature] = "Diese Funktion wird für das ausgewählte Asset nicht unterstützt."
                }
            };
        }
    }
}
