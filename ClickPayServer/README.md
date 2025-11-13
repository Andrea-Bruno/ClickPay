## Click Pay Server

**Click Pay Server** is a support backend for the ClickPay app, designed to provide additional online features accessible via public APIs. This project enables ClickPay to integrate extra functionalities that require server-side logic, extending the capabilities of the mobile app.

### Key Functionality: Merchant QR Code & Payment Flow

One of the main features provided by Click Pay Server is the ability for merchants to generate a unique QR code that identifies them. The payment workflow is as follows:

1. **Merchant Sets Payment Amount:**  
   The merchant uses the ClickPay app to set the amount for the next payment, specifying the currency (e.g., EURC, which is Euro on the Solana blockchain). This information is sent to the Click Pay Server via API as the "amount set for the next payment".

2. **Customer Scans Merchant QR Code:**  
   At the point of sale, the merchant displays their unique ClickPay QR code. The customer scans this code using their ClickPay app.

3. **App Queries Server for Payment Details:**  
   The ClickPay app queries the Click Pay Server API to retrieve the amount and currency set by the merchant for the next payment.

4. **Payment Confirmation and Execution:**  
   If an amount is set, the app opens the relevant wallet (e.g., EURC) and displays the amount with a "Pay Now" button. The customer visually verifies the amount and, with a single click, completes the payment—hence the name "Click Pay". If there are insufficient funds, the app will display an error message.

5. **Instant Blockchain Settlement & Merchant Notification:**  
   Once the payment is executed (typically instantly on blockchain), the merchant receives a confirmation notification.

6. **Timeout and Privacy:**  
   If the payment is not completed within 5 minutes, the server automatically cancels the payment request.  
   No data is stored on the server; for privacy, the server does not know the identities of any parties involved (neither individuals nor legal entities).

---

**Note:**  
Click Pay Server is stateless and privacy-focused, acting only as a temporary relay for payment requests and confirmations between the ClickPay app and its users.

