using System;

namespace Foundation.Infrastructure.YooAsset
{
    /// <summary>
    /// The gate that runs before the first request to the content host asked to stop. It derives from
    /// <see cref="OperationCanceledException"/> because nothing failed: the boot is ending on purpose.
    /// </summary>
    public sealed class ContentUpdateStoppedException : OperationCanceledException
    {
        public ContentUpdateStoppedException()
            : base("The content update was stopped by its gate.")
        {
        }
    }
}
