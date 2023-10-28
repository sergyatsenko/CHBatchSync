using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GVI.ContentHub.Sync.WebJob.Core
{
    /// <summary>
    /// Helper class to create deterministic Guids based on given ID.
    /// </summary>
    public static class GuidUtils
    {
        /// <summary>
        /// Creates a name-based UUID using the algorithm from RFC 4122 §4.3, using SHA1
        /// (version 5). This is useful for creating predictive Guid based on content.
        /// </summary>
        /// <param name="namespaceId">A known namespace to create the UUID within</param>
        /// <param name="name">The name (within the given namespace) to make the Guid from</param>
        /// <returns></returns>
        public static Guid Create(Guid namespaceId, string name)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));

            return Create(namespaceId, Encoding.UTF8.GetBytes(name));
        }

        /// <summary>
        /// Creates a name-based UUID using the algorithm from RFC 4122 §4.3, using SHA1
        /// (version 5). This is useful for creating predictive Guid based on content.
        /// </summary>
        /// <param name="namespaceId">A known namespace to create the UUID within</param>
        /// <param name="nameBytes">The name (within the given namespace) to make the Guid from</param>
        /// <returns>A UUID derived from the namespace and name</returns>
        public static Guid Create(Guid namespaceId, byte[] nameBytes)
        {
            const int version = 5;

            // convert the namespace UUID to network order (step 3)
            byte[] namespaceBytes = namespaceId.ToByteArray();
            SwapByteOrder(namespaceBytes);

            // compute the hash of the namespace ID concatenated with the name (step 4)
            byte[] data = namespaceBytes.Concat(nameBytes).ToArray();
            byte[] hash;
            using (var algorithm = SHA1.Create())
                hash = algorithm.ComputeHash(data);

            // most bytes from the hash are copied straight to the bytes of the new GUID (steps 5-7, 9, 11-12)
            byte[] newGuid = new byte[16];
            Array.Copy(hash, 0, newGuid, 0, 16);

            // set the four most significant bits (bits 12 through 15) of the time_hi_and_version field to the appropriate 4-bit version number from Section 4.1.3 (step 8)
            newGuid[6] = (byte)(newGuid[6] & 0x0F | version << 4);

            // set the two most significant bits (bits 6 and 7) of the clock_seq_hi_and_reserved to zero and one, respectively (step 10)
            newGuid[8] = (byte)(newGuid[8] & 0x3F | 0x80);

            // convert the resulting UUID to local byte order (step 13)
            SwapByteOrder(newGuid);
            return new Guid(newGuid);
        }

        /// <summary>
        /// Converts a Guid to/from network order (MSB-first)
        /// </summary>
        /// <param name="guid"></param>
        private static void SwapByteOrder(byte[] guid)
        {
            SwapBytes(guid, 0, 3);
            SwapBytes(guid, 1, 2);
            SwapBytes(guid, 4, 5);
            SwapBytes(guid, 6, 7);
        }

        private static void SwapBytes(byte[] guid, int left, int right)
        {
            byte temp = guid[left];
            guid[left] = guid[right];
            guid[right] = temp;
        }
    }

}
