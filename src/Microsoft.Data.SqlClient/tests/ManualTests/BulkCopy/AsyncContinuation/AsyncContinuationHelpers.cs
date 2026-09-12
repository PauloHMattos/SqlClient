// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Data;

namespace Microsoft.Data.SqlClient.ManualTests.BulkCopy
{
    // Shared utilities for the async-continuation BulkCopy tests.
    //
    // These tests deliberately drive SqlBulkCopy.WriteToServerAsync through its
    // internal continuation state machine. With small amounts of data the async
    // writes usually complete synchronously (the TDS parser returns a null Task),
    // so the interesting async code paths are never taken. The tests in this
    // folder pair a moderate row count with AsyncDebugScope.ForceAllPends (DEBUG
    // builds only) so that every write pends and the CopyColumnsAsync /
    // CopyRowsAsync / CopyBatchesAsync continuation chain actually runs.
    internal static class AsyncContinuationHelpers
    {
        // A simple three-column shape reused across the tests.
        internal const string ThreeColumnTableDefinition =
            "(col1 int, col2 nvarchar(20), col3 nvarchar(20))";

        // Builds an in-memory DataTable matching ThreeColumnTableDefinition with
        // the requested number of deterministic rows.
        internal static DataTable BuildThreeColumnSource(int rowCount)
        {
            DataTable table = new DataTable("Source");
            table.Columns.Add("col1", typeof(int));
            table.Columns.Add("col2", typeof(string));
            table.Columns.Add("col3", typeof(string));

            for (int i = 0; i < rowCount; i++)
            {
                table.Rows.Add(i, "name_" + i, "tag_" + (i % 7));
            }

            return table;
        }

        // Reads every value of an IDataReader-backed source so the returned
        // reader can be replayed. Not used directly by all tests but handy for
        // building faulted-reader scenarios.
        internal static IEnumerable<object[]> Materialize(IDataReader reader)
        {
            int fieldCount = reader.FieldCount;
            while (reader.Read())
            {
                object[] row = new object[fieldCount];
                reader.GetValues(row);
                yield return row;
            }
        }
    }
}
