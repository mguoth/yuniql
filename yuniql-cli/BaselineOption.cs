﻿using CommandLine;

namespace Yuniql.CLI
{
    //yuniql baseline
    [Verb("baseline", HelpText = "Scripts selected database objects to form your v0.00 schema")]
    public class BaselineOption : BasePlatformOption
    {
    }
}
