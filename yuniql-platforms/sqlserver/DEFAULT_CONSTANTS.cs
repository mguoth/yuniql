﻿namespace Yuniql.SqlServer
{
    /// <summary>
    /// Globa default values.
    /// </summary>
    public static class DEFAULT_CONSTANTS {

        /// <summary>
        /// Default command timeout in seconds.
        /// </summary>
        public const int COMMAND_TIMEOUT_SECS = 30;

        /// <summary>
        /// Default batch size of bulk load operations.
        /// </summary>
        public const int BULK_BATCH_SIZE = 0;
        
        /// <summary>
        /// Default CSV file delimiter.
        /// </summary>
        public const string BULK_DELIMITER = ",";
    }
};