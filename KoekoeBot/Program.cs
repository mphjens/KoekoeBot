// THIS FILE IS A PART OF EMZI0767'S BOT EXAMPLES
//
// --------
// 
// Copyright 2019 Emzi0767
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// --------
//
// This is a voice example. It shows how to properly utilize VoiceNext.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Exceptions;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.VoiceNext;
using KoekoeBot;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace DSPlus.Examples
{

    public class Program
    {

        public static void Main(string[] args)
        {
            // since we cannot make the entry method asynchronous,
            // let's pass the execution to asynchronous code
            var commandQueue = new ConcurrentQueue<KoekoeWebsocketCommand>();
            var prog = new Program();
            var ws = new WebsocketInterface(ref commandQueue);
            
            Parallel.Invoke(new Action[] { 
                () => { KoekoeController.RunBot().GetAwaiter().GetResult(); },
                () => { ws.Serve().GetAwaiter().GetResult(); },
            });
        }      
    }

    // this structure will hold data from config.json
    public struct ConfigJson
    {
        [JsonProperty("token")]
        public string Token { get; private set; }

        [JsonProperty("prefix")]
        public string CommandPrefix { get; private set; }
    }
}