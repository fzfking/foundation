using System;
using Foundation.Save;
using NUnit.Framework;

namespace Foundation.Tests.Save
{
    public sealed class PersistentDataSaveLocationTests
    {
        [TestCase("../outside")]
        [TestCase("nested/slot")]
        [TestCase("nested\\slot")]
        [TestCase("..")]
        [TestCase("")]
        public void ASlotIdThatIsNotAFileName_IsRefused_SoASaveCannotLandOutsideTheSaveFolder(string slotId)
        {
            Assert.Throws<ArgumentException>(() => new PersistentDataSaveLocation().GetSaveFilePath(slotId));
        }

        [Test]
        public void OrdinarySlotIds_NameAFileInTheSaveFolder()
        {
            var location = new PersistentDataSaveLocation();

            StringAssert.EndsWith("save-sample.dat", location.GetSaveFilePath("sample"));
            StringAssert.EndsWith("save.dat", location.GetSaveFilePath(SaveSlot.Default));
        }

        [TestCase("../outside.dat")]
        [TestCase("/tmp/outside.dat")]
        [TestCase("nested\\outside.dat")]
        [TestCase("..")]
        [TestCase("")]
        public void Constructor_RejectsADefaultFileNameThatIsNotAFileName(string fileName)
        {
            Assert.Throws<ArgumentException>(() => new PersistentDataSaveLocation(fileName));
        }

        [Test]
        public void Constructor_RejectsANullDefaultFileName()
        {
            Assert.Throws<ArgumentException>(() => new PersistentDataSaveLocation(null));
        }

        [Test]
        public void Constructor_AcceptsACustomDefaultFileName()
        {
            var location = new PersistentDataSaveLocation("profile.dat");

            StringAssert.EndsWith("profile.dat", location.GetSaveFilePath(SaveSlot.Default));
        }
    }
}
