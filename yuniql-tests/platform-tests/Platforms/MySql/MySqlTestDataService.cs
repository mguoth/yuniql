﻿using Yuniql.Extensibility;
using System.Collections.Generic;
using System.IO;
using MySql.Data.MySqlClient;
using System;
using Yuniql.Core;
using System.Data;

namespace Yuniql.PlatformTests
{
    public class MySqlTestDataService : TestDataServiceBase
    {
        public MySqlTestDataService(IDataService dataService, ITokenReplacementService tokenReplacementService) : base(dataService, tokenReplacementService)
        {
        }

        public override string GetConnectionString(string databaseName)
        {
            var connectionString = EnvironmentHelper.GetEnvironmentVariable("YUNIQL_TEST_CONNECTION_STRING");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ApplicationException("Missing environment variable YUNIQL_TEST_CONNECTION_STRING. See WIKI for developer guides.");
            }

            var result = new MySqlConnectionStringBuilder(connectionString);
            result.Database = databaseName;

            return result.ConnectionString;
        }

        public override bool CheckIfDbExist(string connectionString)
        {
            //use the target user database to migrate, this is part of orig connection string
            var connectionStringBuilder = new MySqlConnectionStringBuilder(connectionString);
            var sqlStatement = $"SELECT 1 FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = '{connectionStringBuilder.Database}';";

            //switch database into master/system database where db catalogs are maintained
            connectionStringBuilder.Database = "INFORMATION_SCHEMA";
            return QuerySingleBool(connectionStringBuilder.ConnectionString, sqlStatement);
        }

        public override bool CheckIfDbObjectExist(string connectionString, string objectName)
        {
            //check from tables, im just lazy to figure out join :)
            var connectionStringBuilder = new MySqlConnectionStringBuilder(connectionString);
            var sqlStatement = $"SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '{connectionStringBuilder.Database}' AND TABLE_NAME = '{objectName}' LIMIT 1;";
            bool result = QuerySingleBool(connectionString, sqlStatement);

            //check from views, im just lazy to figure out join :)
            if (!result)
            {
                sqlStatement = $"SELECT 1 FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_SCHEMA = '{connectionStringBuilder.Database}' AND TABLE_NAME = '{objectName}' LIMIT 1;";
                result = QuerySingleBool(connectionString, sqlStatement);
            }

            //check from functions, im just lazy to figure out join :)
            if (!result)
            {
                sqlStatement = $"SELECT 1 FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_SCHEMA = '{connectionStringBuilder.Database}' AND ROUTINE_NAME = '{objectName}' LIMIT 1;";
                result = QuerySingleBool(connectionString, sqlStatement);
            }

            return result;
        }

        public override string GetSqlForCreateDbObject(string objectName)
        {
            return $@"
CREATE TABLE {objectName} (
	VisitorID INT AUTO_INCREMENT NOT NULL PRIMARY KEY,
	FirstName VARCHAR(255) NULL,
	LastName VARCHAR(255) NULL,
	Address VARCHAR(255) NULL,
	Email VARCHAR(255) NULL
) ENGINE=InnoDB;
";
        }

        //https://stackoverflow.com/questions/42436932/transactions-not-working-for-my-mysql-db
        public override string GetSqlForCreateDbObjectWithError(string objectName)
        {
            return $@"
CREATE TABLE {objectName} (
	VisitorID INT AUTO_INCREMENT NOT NULL PRIMARY_KEY1, #this is a faulty line
	FirstName VARCHAR(255) NULL,
	LastName VARCHAR(255) NULL,
	Address VARCHAR(255) NULL,
	Email [VARCHAR](255) NULL
) ENGINE=InnoDB;
";
        }

        public override string GetSqlForCreateDbObjectWithTokens(string objectName)
        {
            return $@"
CREATE TABLE {objectName}_${{Token1}}_${{Token2}}_${{Token3}} (
	VisitorID INT AUTO_INCREMENT NOT NULL PRIMARY KEY,
	FirstName VARCHAR(255) NULL,
	LastName VARCHAR(255) NULL,
	Address VARCHAR(255) NULL,
	Email VARCHAR(255) NULL
) ENGINE=InnoDB;
";
        }

        public override string GetSqlForCreateBulkTable(string tableName)
        {
            return $@"
CREATE TABLE {tableName}(
	FirstName VARCHAR(50) NOT NULL,
	LastName VARCHAR(50) NOT NULL,
	BirthDate DATETIME NULL
) ENGINE=InnoDB;
";
        }

        public override string GetSqlForSingleLine(string objectName)
        {
            return $@"
CREATE TABLE {objectName} (
	VisitorID INT AUTO_INCREMENT NOT NULL PRIMARY KEY,
	FirstName VARCHAR(255) NULL,
	LastName VARCHAR(255) NULL,
	Address VARCHAR(255) NULL,
	Email VARCHAR(255) NULL
) ENGINE=InnoDB;
";
        }

        public override string GetSqlForSingleLineWithoutTerminator(string objectName)
        {
            return $@"
CREATE TABLE {objectName} (
	VisitorID INT AUTO_INCREMENT NOT NULL PRIMARY KEY,
	FirstName VARCHAR(255) NULL,
	LastName VARCHAR(255) NULL,
	Address VARCHAR(255) NULL,
	Email VARCHAR(255) NULL
) ENGINE=InnoDB
";
        }

        public override string GetSqlForMultilineWithoutTerminatorInLastLine(string objectName1, string objectName2, string objectName3)
        {
            return $@"
CREATE TABLE {objectName1} (
	VisitorID INT AUTO_INCREMENT NOT NULL PRIMARY KEY,
	FirstName VARCHAR(255) NULL,
	LastName VARCHAR(255) NULL,
	Address VARCHAR(255) NULL,
	Email VARCHAR(255) NULL
) ENGINE=InnoDB;

CREATE VIEW {objectName2} AS
SELECT VisitorId, FirstName, LastName, Address, Email
FROM  {objectName1};

CREATE FUNCTION {objectName3} ()
RETURNS INT DETERMINISTIC
BEGIN
    DECLARE total INT;   
    SELECT COUNT(*) INTO total FROM {objectName1};
    RETURN total;
END
";
        }

        public override string GetSqlForMultilineWithTerminatorInsideStatements(string objectName1, string objectName2, string objectName3)
        {
            return $@"
CREATE TABLE {objectName1} (
	VisitorID INT AUTO_INCREMENT NOT NULL PRIMARY KEY,
	FirstName VARCHAR(255) NULL,
	LastName VARCHAR(255) NULL,
	Address VARCHAR(255) NULL,
	Email VARCHAR(255) NULL
) ENGINE=InnoDB;

CREATE VIEW {objectName2} AS
SELECT VisitorId, FirstName, LastName, Address, Email
FROM  {objectName1};

CREATE FUNCTION {objectName3} ()
RETURNS INT DETERMINISTIC
BEGIN   
    DECLARE total INT;   -- this is a comment with terminator ; as part of the sentence;
    
    /*;this is a comment with terminator ; as part of the sentence*/
    SELECT COUNT(*) INTO total FROM {objectName1};
    RETURN total;
END;
";
        }

        public override string GetSqlForMultilineWithError(string objectName1, string objectName2)
        {
            return $@"
CREATE TABLE {objectName1} (
	VisitorID INT AUTO_INCREMENT NOT NULL PRIMARY KEY,
	FirstName VARCHAR(255) NULL,
	LastName VARCHAR(255) NULL,
	Address VARCHAR(255) NULL,
	Email VARCHAR(255) NULL
) ENGINE=InnoDB;

CREATE VIEW {objectName2} AS
SELECT VisitorId, FirstName, LastName, Address, Email
FROM  {objectName1};

SELECT 1/0;
";
        }

        public override void CreateScriptFile(string sqlFilePath, string sqlStatement)
        {
            using var sw = File.CreateText(sqlFilePath);
            sw.WriteLine(sqlStatement);
        }

        public override string GetSqlForCleanup()
        {
            return @"
DROP TABLE script1;
DROP TABLE script2;
DROP TABLE script3;
";
        }

        public override string GetSqlForCreateDbSchema(string schemaName)
        {
            return $@"
CREATE SCHEMA {schemaName};
";
        }

        private Tuple<string, string> GetObjectNameWithSchema(string objectName)
        {
            //check if a non-default dbo schema is used
            var schemaName = "public";
            var newObjectName = objectName;

            if (objectName.IndexOf('.') > 0)
            {
                schemaName = objectName.Split('.')[0];
                newObjectName = objectName.Split('.')[1];
            }

            return new Tuple<string, string>(schemaName.ToLower(), newObjectName.ToLower());
        }

    }
}
