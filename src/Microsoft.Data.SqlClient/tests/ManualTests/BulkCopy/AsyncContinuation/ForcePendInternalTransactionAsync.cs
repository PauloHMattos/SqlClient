// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient.ManualTesting.Tests;
using Microsoft.Data.SqlClient.Tests.Common.Fixtures.DatabaseObjects;
using Xunit;

namespace Microsoft.Data.SqlClient.ManualTests.BulkCopy
{
    // Copies multiple batches with SqlBulkCopyOptions.UseInternalTransaction while
    // forcing all writes to pend. Each batch is wrapped in its own internal
    // transaction that is begun/committed inside the CopyBatchesAsync loop; this
    // verifies that the per-batch begin/commit ordering is preserved through the
    // async continuation path and that all rows commit successfully.
    [Trait("Set", "2")]
    public class ForcePendInternalTransactionAsync
    {
        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.AreConnStringsSetup), nameof(DataTestUtility.IsNotAzureServer))]
        public void Test()
        {
#if DEBUG
            string dstConstr = DataTestUtility.TCPConnectionString;
            Task t = TestAsync(dstConstr);
            t.Wait();
            Assert.True(t.IsCompleted, "Task did not complete! Status: " + t.Status);
#endif
        }

#if DEBUG
        private static async Task TestAsync(string dstConstr)
        {
            const int rowCount = 120;
            const int batchSize = 20; // 6 batches, each its own internal transaction.

            DataTable source = AsyncContinuationHelpers.BuildThreeColumnSource(rowCount);

            using SqlConnection dstConn = new SqlConnection(dstConstr);
            dstConn.Open();

            using Table dstTable = new Table(
                dstConn,
                "BulkCopy_ForcePendInternalTransactionAsync",
                AsyncContinuationHelpers.ThreeColumnTableDefinition);

            using (SqlBulkCopy bulkcopy =
                new SqlBulkCopy(dstConn, SqlBulkCopyOptions.UseInternalTransaction, null))
            {
                bulkcopy.DestinationTableName = dstTable.Name;
                bulkcopy.BatchSize = batchSize;
                bulkcopy.ColumnMappings.Add(0, "col1");
                bulkcopy.ColumnMappings.Add(1, "col2");
                bulkcopy.ColumnMappings.Add(2, "col3");

                using AsyncDebugScope debugScope = new AsyncDebugScope { ForceAllPends = true };

                await bulkcopy.WriteToServerAsync(source);

                DataTestUtility.AssertEqualsWithDescription(
                    (long)rowCount, bulkcopy.RowsCopied64, "Unexpected number of rows copied.");
            }

            Helpers.VerifyResults(dstConn, dstTable.Name, 3, rowCount);
        }
#endif
    }
}
