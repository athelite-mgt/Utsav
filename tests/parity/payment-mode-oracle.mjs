/**
 * Oracle for the PaymentModeClassifier port.
 *
 * Runs the ORIGINAL regex from Node/server/paymentMode.mjs over the same
 * inputs the C# tests assert on, and prints the expected bucket for each.
 * Run this whenever those tests are changed:
 *
 *   node FullStack/tests/parity/payment-mode-oracle.mjs
 *
 * Any disagreement between this output and PaymentModeClassifierTests is a
 * porting bug, not a test that needs relaxing.
 */
import { bucketOf } from "../../../Node/server/paymentMode.mjs";

const INPUTS = [
  // seeded payment_mode lookup labels
  "Cash", "Online - Gpay", "Cheque", "Bank Transfer", "Pending",
  // modes a committee might add later
  "UPI", "PhonePe", "Paytm", "BHIM", "GPay", "G-Pay", "G Pay",
  "NEFT", "IMPS", "RTGS", "Net Banking", "net-banking",
  "Demand Draft", "DD", "Debit Card", "Credit Card", "Online", "check",
  // blank / unknown
  null, "", "   ", "cash in hand", "hand delivered", "collected by Pranab",
  // case handling
  "BANK TRANSFER", "bank transfer", "BaNk TrAnSfEr",
  // documented substring quirks
  "Cashcard", "transferred by hand",
];

for (const input of INPUTS) {
  console.log(`${JSON.stringify(input).padEnd(24)} -> ${bucketOf(input)}`);
}
