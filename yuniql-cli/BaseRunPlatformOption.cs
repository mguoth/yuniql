﻿using CommandLine;
using System.Collections.Generic;

namespace Yuniql.CLI
{

    public class BaseRunPlatformOption : BasePlatformOption
    {
        //yuniql <command> -t v1.05 | --target-version v1.05
        [Option('t', "target-version", Required = false, HelpText = "Target version to migrate into and skipping versions greater")]
        public string TargetVersion { get; set; }

        //yuniql <command> -a true | --auto-create-db true
        [Option('a', "auto-create-db", Required = false, HelpText = "Create database automatically")]
        public bool AutoCreateDatabase { get; set; }

        //yuniql <command> -k "Token1=TokenValue1" -k "Token2=TokenValue2" -k "Token3=TokenValue3" | --token "..." --token "..." --token "..."
        //yuniql <command> -k "Token1=TokenValue1,Token2=TokenValue2,Token3=TokenValue3" | --token "...,...,..."
        [Option('k', "token", Required = false, HelpText = "Replace tokens using the passed key-value pairs", Separator = ',')]
        public IEnumerable<string> Tokens { get; set; } = new List<string>();

        //yuniql <command> --delimeter "," | --delimeter "|"
        [Option("delimiter", Required = false, HelpText = "Bulk import file delimiter", Default = ",")]
        public string Delimiter { get; set; } = ",";
    }
}
