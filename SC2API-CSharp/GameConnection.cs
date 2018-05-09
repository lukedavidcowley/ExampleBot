﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using SC2APIProtocol;

namespace SC2API_CSharp
{
    public class GameConnection
    {
        ProtobufProxy proxy = new ProtobufProxy();
        string address = "127.0.0.1";

        string starcraftExe;
        string starcraftDir;

        public GameConnection()
        {
            readSettings();
        }

        public void StartSC2Instance(int port)
        {
            ProcessStartInfo processStartInfo = new ProcessStartInfo(starcraftExe);
            processStartInfo.Arguments = String.Format("-listen {0} -port {1} -displayMode 0", address, port);
            processStartInfo.WorkingDirectory = Path.Combine(starcraftDir, "Support64");
            Process.Start(processStartInfo);
        }

        public async Task Connect(int port)
        {

            for (int i = 0; i < 40; i++)
            {
                try
                {
                    await proxy.Connect(address, port);
                    return;
                }
                catch (WebSocketException e) { }
                Thread.Sleep(2000);
            }
            throw new Exception("Unable to make a connection.");
        }

        public async Task CreateGame(String mapName, Race opponentRace, Difficulty opponentDifficulty)
        {
            RequestCreateGame createGame = new RequestCreateGame();
            createGame.Realtime = false;

            string mapPath = Path.Combine(starcraftDir, "maps", mapName);
            if (!File.Exists(mapPath))
                throw new Exception("Could not find map at " + mapPath);
            createGame.LocalMap = new LocalMap();
            createGame.LocalMap.MapPath = mapPath;

            PlayerSetup player1 = new PlayerSetup();
            createGame.PlayerSetup.Add(player1);
            player1.Type = PlayerType.Participant;

            PlayerSetup player2 = new PlayerSetup();
            createGame.PlayerSetup.Add(player2);
            player2.Race = opponentRace;
            player2.Type = PlayerType.Computer;
            player2.Difficulty = opponentDifficulty;

            Request request = new Request();
            request.CreateGame = createGame;
            Response response = await proxy.SendRequest(request);
        }

        private void readSettings()
        {
            string myDocuments = Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            string executeInfo = Path.Combine(myDocuments, "Starcraft II", "ExecuteInfo.txt");
            if (File.Exists(executeInfo))
            {
                string[] lines = File.ReadAllLines(executeInfo);
                foreach (string line in lines)
                {
                    string argument = line.Substring(line.IndexOf('=') + 1).Trim();
                    if (line.Trim().StartsWith("executable"))
                    {
                        starcraftExe = argument;
                        starcraftDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(starcraftExe)));
                    }
                }
            }
            else
                throw new Exception("Unable to find ExecuteInfo.txt at " + executeInfo);
        }
        
        public async Task<uint> JoinGame(Race race)
        {
            RequestJoinGame joinGame = new RequestJoinGame();
            joinGame.Race = race;

            joinGame.Options = new InterfaceOptions();
            joinGame.Options.Raw = true;
            joinGame.Options.Score = true;

            Request request = new Request();
            request.JoinGame = joinGame;
            Response response = await proxy.SendRequest(request);
            return response.JoinGame.PlayerId;
        }

        public async Task<uint> JoinGameLadder(Race race, int startPort)
        {
            RequestJoinGame joinGame = new RequestJoinGame();
            joinGame.Race = race;
            
            joinGame.SharedPort = startPort + 1;
            joinGame.ServerPorts = new PortSet();
            joinGame.ServerPorts.GamePort = startPort + 2;
            joinGame.ServerPorts.BasePort = startPort + 3;

            joinGame.ClientPorts.Add(new PortSet());
            joinGame.ClientPorts[0].GamePort = startPort + 4;
            joinGame.ClientPorts[0].BasePort = startPort + 5;

            joinGame.Options = new InterfaceOptions();
            joinGame.Options.Raw = true;
            joinGame.Options.Score = true;

            Request request = new Request();
            request.JoinGame = joinGame;

            Response response = await proxy.SendRequest(request);
            return response.JoinGame.PlayerId;
        }

        public async Task Ping()
        {
            await proxy.Ping();
        }

        public async Task Run(Bot bot, uint playerId)
        {
            
            Request gameInfoReq = new Request();
            gameInfoReq.GameInfo = new RequestGameInfo();

            Response gameInfoResponse = await proxy.SendRequest(gameInfoReq);
            
            while (true)
            {
                Request observationRequest = new Request();
                observationRequest.Observation = new RequestObservation();
                Response response = await proxy.SendRequest(observationRequest);

                ResponseObservation observation = response.Observation;

                if (response.Status == Status.Ended || response.Status == Status.Quit)
                    break;
                
                IEnumerable<SC2APIProtocol.Action> actions = bot.onFrame(gameInfoResponse.GameInfo, observation, playerId);

                Request actionRequest = new Request();
                actionRequest.Action = new RequestAction();
                actionRequest.Action.Actions.AddRange(actions);
                if (actionRequest.Action.Actions.Count > 0)
                    await proxy.SendRequest(actionRequest);

                Request stepRequest = new Request();
                stepRequest.Step = new RequestStep();
                stepRequest.Step.Count = 1;
                await proxy.SendRequest(stepRequest);
            }
        }
        
        public async Task RunSinglePlayer(Bot bot, string map, Race myRace, Race opponentRace, Difficulty opponentDifficulty)
        {
            StartSC2Instance(5678);
            await Connect(5678);
            await CreateGame(map, opponentRace, opponentDifficulty);
            uint playerId = await JoinGame(myRace);
            await Run(bot, playerId);
        }

        public async Task RunLadder(Bot bot, Race myRace, int gamePort, int startPort)
        {
            await Connect(gamePort);
            uint playerId = await JoinGameLadder(myRace, startPort);
            await Run(bot, playerId);
        }

        public async Task RunLadder(Bot bot, Race myRace, string[] args)
        {
            CLArgs clargs = new CLArgs(args);
            await RunLadder(bot, myRace, clargs.GamePort, clargs.StartPort);
        }
    }
}
