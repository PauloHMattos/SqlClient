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
    // Forces every async write to pend (DEBUG only) while copying enough rows to
    // span multiple batches. This guarantees the CopyBatchesAsync -> CopyRowsAsync
    // -> CopyColumnsAsync continuation chain is exercised, which is the code being
    // rewritten to async/await. Verifies all rows land and RowsCopied is correct.
    [Trait("Set", "2")]
    public class ForcePendMultiBatchAsync
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
            const int rowCount = 100;
            const int batchSize = 17; // Deliberately does not divide rowCount evenly.

            DataTable source = AsyncContinuationHelpers.BuildThreeColumnSource(rowCount);

            using SqlConnection dstConn = new SqlConnection(dstConstr);
            dstConn.Open();

            using Table dstTable = new Table(
                dstConn,
                "BulkCopy_ForcePendMultiBatchAsync",
                AsyncContinuationHelpers.ThreeColumnTableDefinition);

            using (SqlBulkCopy bulkcopy = new SqlBulkCopy(dstConn))
            {
                bulkcopy.DestinationTableName = dstTable.Name;
                bulkcopy.BatchSize = batchSize;
                bulkcopy.ColumnMappings.Add(0, "col1");
                bulkcopy.ColumnMappings.Add(1, "col2");
                bulkcopy.ColumnMappings.Add(2, "col3");

                // Force all writes to pend so the async continuation chain runs.
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
