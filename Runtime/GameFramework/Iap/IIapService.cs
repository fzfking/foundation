using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.GameFramework.Iap
{
    public enum IapProductType
    {
        Consumable,
        NonConsumable,
        Subscription
    }

    public sealed class IapProductInfo
    {
        public string ProductId;
        public string FormattedPrice;
        public IapProductType Type;
    }

    public sealed class PurchaseRecord
    {
        public string ProductId;
        public string TransactionId;
        public string ReceiptData;
        public DateTime PurchaseTimeUtc;
    }

    public sealed class PurchaseResult
    {
        public bool Success;
        public PurchaseRecord Purchase;
        public string Error;
    }

    public interface IIapService
    {
        UniTask InitializeAsync(CancellationToken cancellationToken = default);

        UniTask<IReadOnlyList<IapProductInfo>> FetchProductsAsync(
            IReadOnlyList<string> productIds,
            CancellationToken cancellationToken = default);

        UniTask<PurchaseResult> PurchaseAsync(string productId, CancellationToken cancellationToken = default);

        UniTask<IReadOnlyList<PurchaseRecord>> RestorePurchasesAsync(CancellationToken cancellationToken = default);

        UniTask<bool> ConfirmPurchaseAsync(PurchaseRecord purchase, CancellationToken cancellationToken = default);
    }
}
