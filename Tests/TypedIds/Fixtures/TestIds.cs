using Foundation.TypedIds;

namespace Foundation.Tests.TypedIds
{
    // Declared in their own assembly on purpose: the MemoryPack generator cannot see members produced by
    // another source generator, so a typed id used in a [MemoryPackable] type of the same assembly fails to
    // compile. Ids from a referenced assembly (such as AudioEventId) are plain structs with the generated
    // formatter.
    [StableId]
    public partial struct TestScreenId
    {
    }

    [StableId]
    public partial struct TestAudioId
    {
    }

    [RuntimeId]
    public partial struct TestTransactionId
    {
    }
}
