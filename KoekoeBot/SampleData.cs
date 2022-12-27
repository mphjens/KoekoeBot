using System;
using System.Collections.Generic;

namespace KoekoeBot
{
    // Data structure represents a sample within a guild
    public class SampleData
    {
        public string Name;
        public bool enabled;
        public DateTime DateAdded;
        public int PlayCount;
        public List<string> SampleAliases;  // can be used the the play command to address this sample
                                            // we add a number to the alias list when adding a new sample
                                            // we need to enforce that these are unique when adding them
        public string Filename;
    }
}