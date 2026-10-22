using NUnit.Framework;
using SciTech.Collections;
using SciTech.Collections.Immutable;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace SciTech.Rpc.Tests.Core
{
    [TestFixture]
    public class ListExtensionsTests
    {
        // These calls also guard against FindIndex overloads becoming ambiguous for
        // types that implement both IList<T> and IReadOnlyList<T>.
        [Test]
        public void FindIndex_Should_ResolveForListTypes()
        {
            int[] array = [1, 2, 3];
            var collection = new Collection<int> { 1, 2, 3 };
            IList<int> list = new List<int> { 1, 2, 3 };
            ImmutableArray<int> immutableArray = [1, 2, 3];
            ImmutableArrayList<int> immutableArrayList = [1, 2, 3];

            Assert.AreEqual(1, array.FindIndex(i => i == 2));
            Assert.AreEqual(1, collection.FindIndex(i => i == 2));
            Assert.AreEqual(1, list.FindIndex(i => i == 2));
            Assert.AreEqual(1, immutableArray.FindIndex(i => i == 2));
            Assert.AreEqual(1, immutableArrayList.FindIndex(i => i == 2));
            Assert.AreEqual(-1, immutableArrayList.FindIndex(i => i == 4));
        }

        [Test]
        public void FindReadOnlyIndex_Should_FindFirstMatch()
        {
            IReadOnlyList<int> array = new[] { 1, 2, 2 };
            IReadOnlyList<int> list = new List<int> { 1, 2, 2 };
            IReadOnlyList<int> wrapped = new Collection<int> { 1, 2, 2 }.AsReadOnlyList();

            Assert.AreEqual(1, array.FindReadOnlyIndex(i => i == 2));
            Assert.AreEqual(1, list.FindReadOnlyIndex(i => i == 2));
            Assert.AreEqual(1, wrapped.FindReadOnlyIndex(i => i == 2));
            Assert.AreEqual(-1, wrapped.FindReadOnlyIndex(i => i == 4));
        }
    }
}
