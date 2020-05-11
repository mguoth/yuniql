﻿using Yuniql.Extensibility;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.IO;

namespace Yuniql.Core
{
    public class MigrationService : IMigrationService
    {
        private readonly ILocalVersionService _localVersionService;
        private readonly IDataService _dataService;
        private readonly IBulkImportService _bulkImportService;
        private readonly ITokenReplacementService _tokenReplacementService;
        private readonly IDirectoryService _directoryService;
        private readonly IFileService _fileService;
        private readonly ITraceService _traceService;
        private readonly IConfigurationDataService _configurationDataService;

        public MigrationService(
            ILocalVersionService localVersionService,
            IDataService dataService,
            IBulkImportService bulkImportService,
            IConfigurationDataService configurationDataService,
            ITokenReplacementService tokenReplacementService,
            IDirectoryService directoryService,
            IFileService fileService,
            ITraceService traceService)
        {
            this._localVersionService = localVersionService;
            this._dataService = dataService;
            this._bulkImportService = bulkImportService;
            this._tokenReplacementService = tokenReplacementService;
            this._directoryService = directoryService;
            this._fileService = fileService;
            this._traceService = traceService;
            this._configurationDataService = configurationDataService;
        }

        public void Initialize(
            string connectionString,
            int? commandTimeout = null)
        {
            //initialize dependencies
            _dataService.Initialize(connectionString);
            _bulkImportService.Initialize(connectionString);
        }

        /// <summary>
        /// Returns the current migration version applied in target database.
        /// </summary>
        public string GetCurrentVersion()
        {
            return _configurationDataService.GetCurrentVersion();
        }

        /// <summary>
        /// Returns all migration versions applied in the target database
        /// </summary>
        public List<DbVersion> GetAllVersions()
        {
            return _configurationDataService.GetAllVersions();
        }

        /// <summary>
        /// Runs migrations by executing alls scripts in the workspace directory. 
        /// When CSV files are present also run bulk import operations to target database table having same file name.
        /// </summary>
        /// <param name="workingPath">The directory path to migration project.</param>
        /// <param name="targetVersion">The maximum version to run to. When NULL, runs migration to the latest version found in the workspace path.</param>
        /// <param name="autoCreateDatabase">When TRUE, creates the database in the target host.</param>
        /// <param name="tokenKeyPairs">Token kev/value pairs to replace tokens in script files.</param>
        /// <param name="verifyOnly">When TRUE, runs the migration in uncommitted mode. No changes are committed to target database. When NULL, runs migration in atomic mode.</param>
        /// <param name="delimiter">Delimeter character in the CSV bulk import files. When NULL, uses comma.</param>
        /// <param name="commandTimeout">Command timeout in seconds. When NULL, it uses default provider command timeout.</param>
        /// <param name="batchSize">Batch rows to processed when performing bulk import. When NULL, it uses default provider batch size.</param>
        public void Run(
            string workingPath,
            string targetVersion = null,
            bool? autoCreateDatabase = false,
            List<KeyValuePair<string, string>> tokenKeyPairs = null,
            bool? verifyOnly = false,
            string delimiter = null,
            int? commandTimeout = null,
            int? batchSize = null,
            string appliedByTool = null,
            string appliedByToolVersion = null,
            string environmentCode = null
         )
        {
            //validate workspace structure
            _localVersionService.Validate(workingPath);

            //when uncomitted run is not supported, fail migration and throw exceptions
            if (verifyOnly.HasValue && verifyOnly == true && !_dataService.IsAtomicDDLSupported)
            {
                throw new NotSupportedException("Yuniql.Verify is not supported in the target platform. " +
                    "The feature requires support for atomic DDL operations. " +
                    "An atomic DDL operations ensures creation of tables, views and other objects and data are rolledback in case of error. " +
                    "For more information see WIKI.");
            }

            //when no target version specified, we use the latest local version 
            if (string.IsNullOrEmpty(targetVersion))
            {
                targetVersion = _localVersionService.GetLatestVersion(workingPath);
                _traceService.Info($"No explicit target version requested. We'll use latest available locally {targetVersion} on {workingPath}.");
            }

            var connectionInfo = _dataService.GetConnectionInfo();
            var targetDatabaseName = connectionInfo.Database;
            var targetDatabaseServer = connectionInfo.DataSource;

            //create the database, we need this to be outside of the transaction scope
            //in an event of failure, users have to manually drop the auto-created database
            if (autoCreateDatabase.HasValue && autoCreateDatabase == true)
            {
                //we only check if the db exists when --auto-create-db is true
                var targetDatabaseExists = _configurationDataService.IsDatabaseExists();
                if (!targetDatabaseExists)
                {
                    _traceService.Info($"Target database does not exist. Creating database {targetDatabaseName} on {targetDatabaseServer}.");
                    _configurationDataService.CreateDatabase();
                    _traceService.Info($"Created database {targetDatabaseName} on {targetDatabaseServer}.");
                }
            }

            //check if database has been pre-configured to support migration and setup when its not
            var targetDatabaseConfigured = _configurationDataService.IsDatabaseConfigured();
            if (!targetDatabaseConfigured)
            {
                _traceService.Info($"Target database {targetDatabaseName} on {targetDatabaseServer} not yet configured for migration.");
                _configurationDataService.ConfigureDatabase();
                _traceService.Info($"Configured database migration support for {targetDatabaseName} on {targetDatabaseServer}.");
            }

            var dbVersions = _configurationDataService.GetAllVersions()
                .Select(dv => dv.Version)
                .OrderBy(v => v)
                .ToList();

            var targeDatabaseLatest = IsTargetDatabaseLatest(targetVersion);
            if (!targeDatabaseLatest)
            {
                using (var connection = _dataService.CreateConnection())
                {
                    connection.Open();

                    //enclose all executions in a single transaction in case platform supports it
                    if (_dataService.IsAtomicDDLSupported)
                    {
                        _traceService.Debug(@$"Target platform fully supports transactions. Migration will run in single transaction.");

                        using (var transaction = connection.BeginTransaction())
                        {
                            try
                            {
                                RunInternal(connection, transaction);

                                //when true, the execution is an uncommitted transaction 
                                //and only for purpose of testing if all can go well when it run to the target environment
                                if (verifyOnly.HasValue && verifyOnly == true)
                                    transaction.Rollback();
                                else
                                    transaction.Commit();
                            }
                            catch (Exception)
                            {
                                _traceService.Error("Target database will be rolled back to its previous state.");
                                transaction.Rollback();
                                throw;
                            }
                        }
                    }
                    else //otherwise don't use transactions
                    {
                        _traceService.Info(@$"Target platform doesn't reliably support transactions for all commands. Migration will not run in single transaction. Any failure during the migration can prevent automatic completing of migration.");

                        try
                        {
                            RunInternal(connection, null);
                        }
                        catch (Exception)
                        {
                            _traceService.Error("Migration was not running in transaction, therefore roll back of target database to its previous state is not possible. Migration need to be completed manually! Running of Yuniql again, might cause that some scripts will be executed twice!");
                            throw;
                        }
                    }
                }
            }
            else
            {
                _traceService.Info($"Target database runs the latest version already. No changes made at {targetDatabaseName} on {targetDatabaseServer}.");
            }

            //Local method
            void RunInternal(IDbConnection connection, IDbTransaction transaction)
            {
                //check if database has been pre-configured and execute init scripts
                if (!targetDatabaseConfigured)
                {
                    //runs all scripts in the _init folder
                    RunNonVersionScripts(connection, transaction, Path.Combine(workingPath, "_init"), tokenKeyPairs, delimiter: delimiter, commandTimeout: commandTimeout, environmentCode: environmentCode);
                    _traceService.Info($"Executed script files on {Path.Combine(workingPath, "_init")}");
                }

                //checks if target database already runs the latest version and skips work if it already is
                //runs all scripts in the _pre folder and subfolders
                RunNonVersionScripts(connection, transaction, Path.Combine(workingPath, "_pre"), tokenKeyPairs, delimiter: delimiter, commandTimeout: commandTimeout, environmentCode: environmentCode);
                _traceService.Info($"Executed script files on {Path.Combine(workingPath, "_pre")}");

                //runs all scripts int the vxx.xx folders and subfolders
                RunVersionScripts(connection, transaction, dbVersions, workingPath, targetVersion, tokenKeyPairs, delimiter: delimiter, commandTimeout: commandTimeout, batchSize: batchSize, appliedByTool: appliedByTool, appliedByToolVersion: appliedByToolVersion, environmentCode: environmentCode);

                //runs all scripts in the _draft folder and subfolders
                RunNonVersionScripts(connection, transaction, Path.Combine(workingPath, "_draft"), tokenKeyPairs, delimiter: delimiter, commandTimeout: commandTimeout, environmentCode: environmentCode);
                _traceService.Info($"Executed script files on {Path.Combine(workingPath, "_draft")}");

                //runs all scripts in the _post folder and subfolders
                RunNonVersionScripts(connection, transaction, Path.Combine(workingPath, "_post"), tokenKeyPairs, delimiter: delimiter, commandTimeout: commandTimeout, environmentCode: environmentCode);
                _traceService.Info($"Executed script files on {Path.Combine(workingPath, "_post")}");
            }
        }

        private bool IsTargetDatabaseLatest(string targetVersion)
        {
            var dbcv = _configurationDataService.GetCurrentVersion();
            if (string.IsNullOrEmpty(dbcv)) return false;

            var cv = new LocalVersion(dbcv);
            var tv = new LocalVersion(targetVersion);
            return string.Compare(cv.SemVersion, tv.SemVersion) == 1 || //db has more updated than local version
                string.Compare(cv.SemVersion, tv.SemVersion) == 0;      //db has the same version as local version
        }

        private void RunNonVersionScripts(
            IDbConnection connection,
            IDbTransaction transaction,
            string workingPath,
            List<KeyValuePair<string, string>> tokenKeyPairs = null,
            string delimiter = null,
            int? commandTimeout = null,
            string environmentCode = null
        )
        {
            var sqlScriptFiles = _directoryService.GetAllFiles(workingPath, "*.sql").ToList();
            sqlScriptFiles = _directoryService.FilterFiles(workingPath, environmentCode, sqlScriptFiles).ToList();
            _traceService.Info($"Found the {sqlScriptFiles.Count} script files on {workingPath}");
            _traceService.Info($"{string.Join(@"\r\n\t", sqlScriptFiles.Select(s => new FileInfo(s).Name))}");

            //execute all script files in the target folder
            sqlScriptFiles.Sort();
            sqlScriptFiles.ForEach(scriptFile =>
            {
                //https://stackoverflow.com/questions/25563876/executing-sql-batch-containing-go-statements-in-c-sharp/25564722#25564722
                var sqlStatementRaw = _fileService.ReadAllText(scriptFile);
                var sqlStatements = _dataService.BreakStatements(sqlStatementRaw)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                sqlStatements.ForEach(sqlStatement =>
                {
                    //replace tokens with values from the cli
                    sqlStatement = _tokenReplacementService.Replace(tokenKeyPairs, sqlStatement);

                    _traceService.Debug($"Executing sql statement as part of : {scriptFile}{Environment.NewLine}{sqlStatement}");

                    _configurationDataService.ExecuteSql(
                        connection: connection,
                        commandText: sqlStatement,
                        transaction: transaction,
                        commandTimeout: commandTimeout,
                        traceService: _traceService);
                });

                _traceService.Info($"Executed script file {scriptFile}.");
            });
        }

        private void RunVersionScripts(
            IDbConnection connection,
            IDbTransaction transaction,
            List<string> dbVersions,
            string workingPath,
            string targetVersion,
            List<KeyValuePair<string, string>> tokenKeyPairs = null,
            string delimiter = null,
            int? commandTimeout = null,
            int? batchSize = null,
            string appliedByTool = null,
            string appliedByToolVersion = null,
            string environmentCode = null
        )
        {
            //excludes all versions already executed
            var versionDirectories = _directoryService.GetDirectories(workingPath, "v*.*")
                .Where(v => !dbVersions.Contains(new DirectoryInfo(v).Name))
                .ToList();

            //exclude all versions greater than the target version
            if (!string.IsNullOrEmpty(targetVersion))
            {
                versionDirectories.RemoveAll(v =>
                {
                    var cv = new LocalVersion(new DirectoryInfo(v).Name);
                    var tv = new LocalVersion(targetVersion);

                    return string.Compare(cv.SemVersion, tv.SemVersion) == 1;
                });
            }

            //execute all sql scripts in the version folders
            if (versionDirectories.Any())
            {
                versionDirectories.Sort();
                versionDirectories.ForEach(versionDirectory =>
                {
                    try
                    {
                        //run scripts in all sub-directories
                        List<string> scriptSubDirectories = null;

                        //check for transaction directory
                        bool isExplicitTransactionDefined = false;
                        string transactionDirectory = Path.Combine(versionDirectory, "_transaction");
                        if (_directoryService.Exists(transactionDirectory))
                        {
                            if (_dataService.IsAtomicDDLSupported)
                            {
                                throw new YuniqlMigrationException(@$"The version directory ""{versionDirectory}"" can't contain ""_transaction"" subdirectory for selected target platform, because the whole migration is already running in single transaction.");
                            }
                            if (_directoryService.GetDirectories(versionDirectory, "*").Count() > 1)
                            {
                                throw new YuniqlMigrationException(@$"The version directory ""{versionDirectory}"" containing ""_transaction"" subdirectory can't contain other subdirectories");
                            }
                            else if (_directoryService.GetFiles(versionDirectory, "*.*").Count() > 0)
                            {
                                throw new YuniqlMigrationException(@$"The version directory ""{versionDirectory}"" containing ""_transaction"" subdirectory can't contain files");
                            }

                            isExplicitTransactionDefined = true;
                            scriptSubDirectories = _directoryService.GetAllDirectories(transactionDirectory, "*").ToList();
                        }
                        else
                        {
                            scriptSubDirectories = _directoryService.GetAllDirectories(versionDirectory, "*").ToList();
                        }

                        if (isExplicitTransactionDefined)
                        {
                            string versionName = new DirectoryInfo(versionDirectory).Name;

                            //run scripts within a single transaction
                            _traceService.Info(@$"The ""_transaction"" directory has been detected and therefore ""{versionName}"" version scripts will run in single transaction. The rollback will not be reliable in case the version scripts contain commands causing implicit commit (e.g. DDL)!");

                            using (var transaction = connection.BeginTransaction())
                            {
                                try
                                {
                                    RunVersionScriptsInternal(scriptSubDirectories, transactionDirectory, versionDirectory);

                                    transaction.Commit();

                                    _traceService.Info(@$"Target database has been commited after running ""{versionName}"" version scripts.");
                                }
                                catch (Exception)
                                {
                                    _traceService.Error(@$"Target database will be rolled back to the state before running ""{versionName}"" version scripts.");
                                    transaction.Rollback();
                                    throw;
                                }
                            }
                        }
                        else //run scripts without transaction
                        {
                            RunVersionScriptsInternal(scriptSubDirectories, versionDirectory, versionDirectory);
                        }
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                });
            }
            else
            {
                var connectionInfo = _dataService.GetConnectionInfo();
                _traceService.Info($"Target database is updated. No migration step executed at {connectionInfo.Database} on {connectionInfo.DataSource}.");
            }

            void RunVersionScriptsInternal(List<string> scriptSubDirectories, string scriptDirectory, string versionDirectory)
            {
                scriptSubDirectories.Sort();
                scriptSubDirectories.ForEach(scriptSubDirectory =>
                {
                    //run all scripts in the current version folder
                    RunSqlScripts(connection, transaction, workingPath, scriptSubDirectory, tokenKeyPairs, commandTimeout, environmentCode);

                    //import csv files into tables of the the same filename as the csv
                    RunBulkImport(connection, transaction, workingPath, scriptSubDirectory, delimiter, batchSize, commandTimeout, environmentCode);
                });

                //run all scripts in the current version folder
                RunSqlScripts(connection, transaction, workingPath, scriptDirectory, tokenKeyPairs, commandTimeout, environmentCode);

                //import csv files into tables of the the same filename as the csv
                RunBulkImport(connection, transaction, workingPath, scriptDirectory, delimiter, batchSize, commandTimeout, environmentCode);

                //update db version
                var versionName = new DirectoryInfo(versionDirectory).Name;
                _configurationDataService.InsertVersion(connection, transaction, versionName,
                    commandTimeout: commandTimeout,
                    appliedByTool: appliedByTool,
                    appliedByToolVersion: appliedByToolVersion);

                _traceService.Info($"Completed migration to version {versionDirectory}");
            }
        }

        private void RunBulkImport(
            IDbConnection connection,
            IDbTransaction transaction,
            string workingPath,
            string scriptDirectory,
            string delimiter = null,
            int? batchSize = null,
            int? commandTimeout = null,
            string environmentCode = null
        )
        {
            //execute all script files in the version folder
            var bulkFiles = _directoryService.GetFiles(scriptDirectory, "*.csv").ToList();
            bulkFiles = _directoryService.FilterFiles(workingPath, environmentCode, bulkFiles).ToList();
            _traceService.Info($"Found the {bulkFiles.Count} bulk files on {scriptDirectory}");
            _traceService.Info($"{string.Join(@"\r\n\t", bulkFiles.Select(s => new FileInfo(s).Name))}");

            bulkFiles.Sort();
            bulkFiles.ForEach(csvFile =>
            {
                _bulkImportService.Run(connection, transaction, csvFile, delimiter, batchSize: batchSize, commandTimeout: commandTimeout);
                _traceService.Info($"Imported bulk file {csvFile}.");
            });
        }

        private void RunSqlScripts(
            IDbConnection connection,
            IDbTransaction transaction,
            string workingPath,
            string scriptDirectory,
            List<KeyValuePair<string, string>> tokenKeyPairs = null,
            int? commandTimeout = null,
            string environmentCode = null
        )
        {
            //TODO: Filter out scripts when environment code is used
            var sqlScriptFiles = _directoryService.GetFiles(scriptDirectory, "*.sql").ToList();
            sqlScriptFiles = _directoryService.FilterFiles(workingPath, environmentCode, sqlScriptFiles).ToList();
            _traceService.Info($"Found the {sqlScriptFiles.Count} script files on {scriptDirectory}");
            _traceService.Info($"{string.Join(@"\r\n\t", sqlScriptFiles.Select(s => new FileInfo(s).Name))}");

            //execute all script files in the version folder
            sqlScriptFiles.Sort();
            sqlScriptFiles
                .ForEach(scriptFile =>
            {
                var sqlStatementRaw = _fileService.ReadAllText(scriptFile);
                var sqlStatements = _dataService.BreakStatements(sqlStatementRaw)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                ;
                sqlStatements.ForEach(sqlStatement =>
                {
                    sqlStatement = _tokenReplacementService.Replace(tokenKeyPairs, sqlStatement);

                    _traceService.Debug($"Executing sql statement as part of : {scriptFile}{Environment.NewLine}{sqlStatement}");
                    _configurationDataService.ExecuteSql(
                        connection: connection,
                        commandText: sqlStatement,
                        transaction: transaction,
                        commandTimeout: commandTimeout,
                        traceService: _traceService);
                });

                _traceService.Info($"Executed script file {scriptFile}.");
            });
        }

        /// <summary>
        /// Executes erase scripts presentin _erase directory and subdirectories.
        /// </summary>
        /// <param name="workingPath">The directory path to migration project.</param>
        /// <param name="tokens">Token kev/value pairs to replace tokens in script files.</param>
        /// <param name="commandTimeout">Command timeout in seconds.</param>
        public void Erase(
            string workingPath,
            List<KeyValuePair<string, string>> tokenKeyPairs = null,
            int? commandTimeout = null,
            string environmentCode = null
        )
        {
            //enclose all executions in a single transaction
            using (var connection = _dataService.CreateConnection())
            {
                connection.KeepOpen();

                //enclose all executions in a single transaction in case platform supports it
                if (_dataService.IsAtomicDDLSupported)
                {
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            //runs all scripts in the _erase folder
                            RunNonVersionScripts(connection, transaction, Path.Combine(workingPath, "_erase"), tokenKeyPairs: tokenKeyPairs, delimiter: DefaultConstants.Delimiter, commandTimeout: commandTimeout, environmentCode: environmentCode);
                            _traceService.Info($"Executed script files on {Path.Combine(workingPath, "_erase")}");

                            transaction.Commit();
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
                else //otherwise don't use transactions
                {
                    //runs all scripts in the _erase folder
                    RunNonVersionScripts(connection, null, Path.Combine(workingPath, "_erase"), tokenKeyPairs: tokenKeyPairs, delimiter: DefaultConstants.Delimiter, commandTimeout: commandTimeout, environmentCode: environmentCode);
                    _traceService.Info($"Executed script files on {Path.Combine(workingPath, "_erase")}");
                }
            }
        }
    }
}
