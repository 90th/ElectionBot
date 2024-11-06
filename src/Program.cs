using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

public class Program {
    private static readonly HttpClient httpClient = new HttpClient();

    private static readonly DiscordSocketClient client = new DiscordSocketClient(new DiscordSocketConfig {
        GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent
    });

    private static int democratVotes = 0;
    private static int republicanVotes = 0;
    private static bool winnerAnnounced = false;
    private static List<Seat> stateDetails = new List<Seat>();

    public static async Task Main(string[] args) {
        client.Log += Log;
        client.MessageReceived += MessageReceived;

        string token = "";
        await client.LoginAsync(TokenType.Bot, token);
        await client.StartAsync();

        _ = Task.Run(BackgroundUpdateLoop);

        await Task.Delay(-1);
    }

    private static async Task MessageReceived(SocketMessage message) {
        if (message is SocketUserMessage userMessage && !message.Author.IsBot) {
            if (userMessage.Content.StartsWith("!")) {
                string command = userMessage.Content.Substring(1).ToLower();

                switch (command) {
                    case "results":
                        await ShowElectoralVotes(message.Channel);
                        break;

                    case "detailedresults":
                        await ShowDetailedResults(message.Channel);
                        break;

                    default:
                        break;
                }
            }
        }
    }

    private static async Task BackgroundUpdateLoop() {
        while (true) {
            await UpdateElectoralVotes();
            await CheckForWinnerAnnouncement();

            await Task.Delay(TimeSpan.FromMinutes(5));
        }
    }

    private static async Task CheckForWinnerAnnouncement() {
        if (winnerAnnounced) return;

        ISocketMessageChannel channel = client.GetChannel(1108471041862406207) as ISocketMessageChannel;

        if (democratVotes >= 270) {
            await channel.SendMessageAsync("@everyone Your PRESIDENT is Kamala!");
            winnerAnnounced = true;
            Environment.Exit(1);
        } else if (republicanVotes >= 270) {
            await channel.SendMessageAsync("@everyone Your PRESIDENT is Trump!");
            winnerAnnounced = true;
            Environment.Exit(1);
        }
    }

    private static async Task ShowElectoralVotes(ISocketMessageChannel channel) {
        await UpdateElectoralVotes();

        Color embedColor;
        string winnerMessage = null;

        if (democratVotes >= 270) {
            winnerMessage = "@everyone Your PRESIDENT is Kamala!";
            embedColor = Color.Blue;
        } else if (republicanVotes >= 270) {
            winnerMessage = "@everyone Your PRESIDENT is Trump!";
            embedColor = Color.Red;
        } else if (democratVotes > republicanVotes) {
            embedColor = Color.Blue;
        } else if (republicanVotes > democratVotes) {
            embedColor = Color.Red;
        } else {
            embedColor = Color.DarkGrey;
        }
        var embed = new EmbedBuilder()
            .WithTitle("Total Votes")
            .WithColor(embedColor)
            .WithCurrentTimestamp();

        if (democratVotes == republicanVotes) {
            embed.AddField("Total Votes - It's a Tie!", $"Democrats: {democratVotes}\nRepublicans: {republicanVotes}", false)
                .WithImageUrl("https://i.giphy.com/media/v1.Y2lkPTc5MGI3NjExYjliMTh4d2czNjNkdnZuZGZ1aGM2eDB4NGdrZmVqbzJ0cHBiMm5uNyZlcD12MV9pbnRlcm5hbF9naWZfYnlfaWQmY3Q9Zw/pGun9dgMQwSgSzryHp/giphy.gif"); // Show the tie image or GIF
        } else {
            embed.AddField("Democrats", democratVotes.ToString(), true)
                .AddField("Republicans", republicanVotes.ToString(), true)
                .WithThumbnailUrl(democratVotes > republicanVotes ? "https://seeklogo.com/images/D/democratic-donkey-logo-D6017B7EA8-seeklogo.com.png" : "https://cdn.discordapp.com/attachments/1175598999856750632/1303489239945580625/Republicanlogo.svg.png?ex=672bf074&is=672a9ef4&hm=af80a06aa09949ef0ed1c5abb835da376fbc7861577781f370a10ba2906b0394&"); // Show the image of the leading party
        }

        await channel.SendMessageAsync(embed: embed.Build());

        if (winnerMessage != null) {
            await channel.SendMessageAsync(winnerMessage);
            await Task.Delay(222);
            Environment.Exit(1);
        }
    }

    private static async Task ShowDetailedResults(ISocketMessageChannel channel) {
        await UpdateElectoralVotes();

        var stateResults = new Dictionary<string, (int totalEVotes, string winnerParty)>();

        foreach (var seat in stateDetails) {
            if (stateResults.ContainsKey(seat.StateName)) {
                stateResults[seat.StateName] = (
                    stateResults[seat.StateName].totalEVotes + seat.EVotes,
                    seat.WinnerParty
                );
            } else {
                stateResults.Add(seat.StateName, (seat.EVotes, seat.WinnerParty));
            }
        }

        var statePairs = new List<(string, string)>();

        foreach (var state in stateResults) {
            string stateInfo = $"E-Votes: {state.Value.totalEVotes}, Winner Party: {state.Value.winnerParty}";
            statePairs.Add((state.Key, stateInfo));
        }

        int batchSize = 12;

        for (int i = 0; i < statePairs.Count; i += batchSize) {
            var embed = new EmbedBuilder()
                .WithTitle("Detailed State Results")
                .WithColor(Color.Green)
                .WithCurrentTimestamp();

            for (int j = i; j < i + batchSize && j < statePairs.Count; j++) {
                var (stateName, stateInfo) = statePairs[j];
                embed.AddField(stateName, stateInfo, true);
            }

            await channel.SendMessageAsync(embed: embed.Build());
        }
    }

    private static async Task UpdateElectoralVotes() {
        try {
            string url = "https://www.270towin.com/election-results-live/php/get_presidential_results.php?election_year=2024";

            HttpResponseMessage response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string jsonData = await response.Content.ReadAsStringAsync();

            var jsonObject = JsonSerializer.Deserialize<JsonDocument>(jsonData);
            var seatsElement = jsonObject.RootElement.GetProperty("seats");

            democratVotes = 0;
            republicanVotes = 0;
            stateDetails.Clear();

            foreach (var stateProperty in seatsElement.EnumerateObject()) {
                foreach (var seatJson in stateProperty.Value.EnumerateArray()) {
                    var seat = JsonSerializer.Deserialize<Seat>(seatJson.GetRawText());

                    stateDetails.Add(seat);

                    if (seat.WinnerParty == "D") {
                        democratVotes += seat.EVotes;
                    } else if (seat.WinnerParty == "R") {
                        republicanVotes += seat.EVotes;
                    }
                }
            }
        } catch (Exception ex) {
            Console.WriteLine("An error occurred: " + ex.Message);
        }
    }

    private static Task Log(LogMessage log) {
        Console.WriteLine(log);
        return Task.CompletedTask;
    }
}

public class Seat {
    public int SeatId { get; set; }

    [JsonPropertyName("state_fips_code")]
    public string StateFipsCode { get; set; }

    [JsonPropertyName("state_abbr")]
    public string StateAbbr { get; set; }

    [JsonPropertyName("state_name")]
    public string StateName { get; set; }

    [JsonPropertyName("e_votes")]
    public int EVotes { get; set; }

    [JsonPropertyName("winner_party")]
    public string WinnerParty { get; set; }
}