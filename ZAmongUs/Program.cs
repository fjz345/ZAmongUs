// Filip Zachrisson (FjZ345)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;
using Discord.Rest;
using Discord.WebSocket;

namespace ZAmongUs
{
    class Program
    {
        static private DiscordSocketClient _client;
        static private CommandService _command;

        static private string lastSignupChannelName = "bot-spam";

        // Default message
        static public string defaultBeforeBlock = "@everyone";
        static public string defaultHeader = "Ａｍｏｎｇ Ｕｓ game comming up next.";
        static public string defaultFooter = "Sign-up by reacting!";


        static public IUserMessage lastSignup;
        static string lastSignup_header = defaultHeader;
        static string lastSignup_footer = defaultFooter;
        static string lastSignup_date;
        static string lastSignup_time;
        static List<ulong> lastSignup_acceptedUsers;
        static List<ulong> lastSignup_declinedUsers;
        static Emoji lastSignup_declineEmoji;
        static Emoji lastSignup_acceptEmoji;

        static SocketTextChannel signupChannel;

        static public string fileNameToken = "token.txt";
        static public string fileNameError = "error.txt";
        static public string fileNameLastSignUp = "signup.txt";

        // Emojis
        // :white_check_mark:
        static public Emoji acceptEmoji = new Emoji("\u2705");
        // :no_entry:
        static public Emoji declineEmoji = new Emoji("\u26D4");

        public class CheckRole : PreconditionAttribute
        {
            private List<string> _roles;

            public CheckRole(params string[] roles)
            {
                _roles = roles.ToList();
            }

            public async override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider provider)
            {
                var user = context.User as IGuildUser;
                var discordRoles = context.Guild.Roles.Where(gr => _roles.Any(r => gr.Name == r));

                foreach (var role in discordRoles)
                {
                    var userInRole = user.RoleIds.Any(ri => ri == role.Id);

                    if (userInRole)
                    {
                        return await Task.FromResult(PreconditionResult.FromSuccess());
                    }
                }
                    
                Console.WriteLine($"You do not have permission to use this role: {context.User.Username}");

                return await Task.FromResult(PreconditionResult.FromError($"You do not have permission to use this role: {context.User.Username}"));
            }
        }

        public static void Main(string[] args)
        => new Program().MainAsync().GetAwaiter().GetResult();
        public async Task MainAsync()
        {
            _client = new DiscordSocketClient();
            _command = new CommandService();

            _client.Log += LogAsync;
            _command.Log += LogAsync;

            // Setup .txt files
            await File.WriteAllTextAsync(fileNameError, "");
            var token = await File.ReadAllTextAsync(fileNameToken);



            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
            await InstallCommandsAsync();

            lastSignup_acceptedUsers = new List<ulong>();
            lastSignup_declinedUsers = new List<ulong>();

            lastSignup_acceptEmoji = acceptEmoji;
            lastSignup_declineEmoji = declineEmoji;

            _client.ReactionAdded += ReactionAdded;
            _client.ReactionRemoved += ReactionRemoved;
            _client.GuildAvailable += GuildAvailable;

            // Block this task until the program is closed.
            await Task.Delay(-1);
        }

        private async Task GuildAvailable(SocketGuild guild)
        {
            // Find sign-up channel
            foreach (var channel in guild.TextChannels)
            {
                if(channel.Name.Equals(lastSignupChannelName))
                {
                    signupChannel = channel;
                    string errorMsg = $"Channel set to: {channel.Name}";
                    await File.AppendAllTextAsync(fileNameError, errorMsg + '\n');
                    Console.WriteLine(errorMsg);

                    // Load last signup message.
                    var lastSignupFromFile = await File.ReadAllLinesAsync(fileNameLastSignUp);
                    if (lastSignupFromFile != null && lastSignupFromFile.Length != 0)
                    {
                        // first line is id
                        // second line is date
                        // third line is time
                        string date;
                        string time;
                        ulong id = Convert.ToUInt64(lastSignupFromFile[0]);
                        try
                        {
                            date = lastSignupFromFile[1];
                        }
                        catch (System.IndexOutOfRangeException e)
                        {
                            // Don't load last signup
                            return;
                        }
                        try
                        {
                            time = lastSignupFromFile[2];
                        }
                        catch (System.IndexOutOfRangeException e)
                        {
                            // Don't load last signup
                            return;
                        }
                        
                        

                        // ID-----------------------------------------------------------
                        IMessage Message = await signupChannel.GetMessageAsync(id);

                        lastSignup = (RestUserMessage)Message;

                        try
                        {
                            errorMsg = $"Last Signup set to: {lastSignup.Id}";
                        }
                        catch (System.NullReferenceException e)
                        {
                            // Don't load last signup
                            return;
                        }
                        
                        await File.AppendAllTextAsync(fileNameError, errorMsg + '\n');
                        Console.WriteLine(errorMsg);

                        // Date-----------------------------------------------------------
                        lastSignup_date = date;

                        // Time-----------------------------------------------------------
                        lastSignup_time = time;

                        // Load reactions
                        ReadAllReactions(lastSignup);
                    }
                }
            }

            return;
        }

        static public async void ReadAllReactions(IMessage message)
        {
            // Clear arrays
            lastSignup_acceptedUsers.Clear();
            lastSignup_declinedUsers.Clear();

            var accepts = await message.GetReactionUsersAsync(acceptEmoji, 15).FlattenAsync();
            var declines = await message.GetReactionUsersAsync(declineEmoji, 15).FlattenAsync();
            
            foreach(var user in accepts)
            {
                if(!user.IsBot)
                {
                    lastSignup_acceptedUsers.Add(user.Id);
                }
            }
            
            foreach(var user in declines)
            {
                if (!user.IsBot)
                {
                    lastSignup_declinedUsers.Add(user.Id);
                }
            }
        }

        private async Task LogAsync(LogMessage message)
        {
            string errorMsg = "";
            if (message.Exception is CommandException cmdException)
            {
                errorMsg += $"[Command/{message.Severity}] {cmdException.Command.Aliases.First()}"
                    + $" failed to execute in {cmdException.Context.Channel}.";

                errorMsg += cmdException;
            }
            else
            {
                errorMsg = $"[General/{message.Severity}] {message}";
            }

            await File.AppendAllTextAsync(fileNameError, errorMsg + '\n');
            Console.WriteLine(errorMsg);

            return;
        }


        private async Task ReactionAdded(Cacheable<IUserMessage, UInt64> message, ISocketMessageChannel channel, SocketReaction reaction)
        {
            // Don't react to own reactions
            if (reaction.UserId == _client.CurrentUser.Id)
            {
                return;
            }

            // Don't react if no sign up exists
            if (lastSignup == null)
            {
                return;
            }

            if (message.Id == lastSignup.Id)
            {
                var msg = message.Value;
                await AddReactionLastSignup(msg, reaction);
            }
        }

        private async Task ReactionRemoved(Cacheable<IUserMessage, UInt64> message, ISocketMessageChannel channel, SocketReaction reaction)
        {
            // Don't react to own reactions
            if (reaction.UserId == _client.CurrentUser.Id)
            {
                return;
            }

            // Don't react if no sign up exists
            if (lastSignup == null)
            {
                return;
            }

            if (message.Id == lastSignup.Id)
            {
                // Add users to lastSigned message
                var msg = message.Value;

                await RemoveReactionLastSignup(msg, reaction);
            }
        }

        static public string BuildSignupMessage()
        {
            string formatedAcceptedUsers = "";
            // Format accepted users
            for(var i = 0; i < lastSignup_acceptedUsers.Count; i++)
            {
                var user = _client.GetUser(lastSignup_acceptedUsers[i]);
                formatedAcceptedUsers += $"\t{i} " + lastSignup_acceptEmoji + " " + user.Username + "\n";
            }

            string formatedDeclinedUsers = "";
            // Format declined
            for (var i = 0; i < lastSignup_declinedUsers.Count; i++)
            {
                var user = _client.GetUser(lastSignup_declinedUsers[i]);
                formatedDeclinedUsers += $"\t{i} " + lastSignup_declineEmoji + " " + user.Username + "\n";
            }

            string sendMessage = $"{defaultBeforeBlock}" +
                $"```" +
                $"{lastSignup_header}\n" +
                $"\n" +
                $"Datum: {lastSignup_date}\n" +
                $"Tid: {lastSignup_time}\n" +

                $"\n________________________Signed:________________________\n\n" +
                $"____Accepted:____\n" +
                $"{formatedAcceptedUsers}\n" +

                $"____Declined:____\n" +
                $"{formatedDeclinedUsers}\n" +
                $"_______________________________________________________\n\n" +

                $"{lastSignup_footer}\n" +
                $"```";

            return sendMessage;
        }

        public async Task AddReactionLastSignup(IMessage message, SocketReaction reaction)
        {
            // if not signed
            bool isUserAccept = lastSignup_acceptedUsers.Count(u => u == reaction.UserId) == 1;
            bool isUserDeny = lastSignup_declinedUsers.Count(u => u == reaction.UserId) == 1;

            // Don't accept multiple inputs
            if (isUserAccept || isUserDeny)
            {
                return;
            }

            if (reaction.Emote.Equals(lastSignup_acceptEmoji))
            {
                lastSignup_acceptedUsers.Add(reaction.UserId);
            }
            else if (reaction.Emote.Equals(lastSignup_declineEmoji))
            {
                lastSignup_declinedUsers.Add(reaction.UserId);
            }
            else
            {
                return;
            }

            Console.WriteLine("User Added Reaction");

            string sendMessage = BuildSignupMessage();

            // Send message
            await lastSignup.ModifyAsync(x => x.Content = sendMessage);
        }

        public async Task RemoveReactionLastSignup(IMessage message, SocketReaction reaction)
        {
            // if not signed
            bool isUserAccept = lastSignup_acceptedUsers.Count(u => u == reaction.UserId) == 1;
            bool isUserDeny = lastSignup_declinedUsers.Count(u => u == reaction.UserId) == 1;

            // if user don't got a reaction, return
            if (!(isUserAccept || isUserDeny))
            {
                return;
            }

            if (isUserAccept && reaction.Emote.Equals(lastSignup_acceptEmoji))
            {
                lastSignup_acceptedUsers.Remove(reaction.UserId);
            }
            else if (isUserDeny && reaction.Emote.Equals(lastSignup_declineEmoji))
            {
                lastSignup_declinedUsers.Remove(reaction.UserId);
            }
            else
            {
                return;
            }

            Console.WriteLine("User Removed Reaction");

            string sendMessage = BuildSignupMessage();

            // Send message
            await lastSignup.ModifyAsync(x => x.Content = sendMessage);
        }


        public async Task InstallCommandsAsync()
        {
            // Hook the MessageReceived event into our command handler
            _client.MessageReceived += HandleCommandAsync;

            // Here we discover all of the command modules in the entry 
            // assembly and load them. Starting from Discord.NET 2.0, a
            // service provider is required to be passed into the
            // module registration method to inject the 
            // required dependencies.
            //
            // If you do not use Dependency Injection, pass null.
            // See Dependency Injection guide for more information.
            await _command.AddModulesAsync(assembly: Assembly.GetEntryAssembly(),
                                            services: null);
        }

        private async Task HandleCommandAsync(SocketMessage messageParam)
        {
            // Don't process the command if it was a system message
            var message = messageParam as SocketUserMessage;
            if (message == null) return;

            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;

            // Determine if the message is a command based on the prefix and make sure no bots trigger commands
            if (!(message.HasCharPrefix('!', ref argPos) ||
                message.HasMentionPrefix(_client.CurrentUser, ref argPos)) ||
                message.Author.IsBot)
                return;

            // Create a WebSocket-based command context based on the message
            var context = new SocketCommandContext(_client, message);

            // Execute the command with the command context we just
            // created, along with the service provider for precondition checks.
            await _command.ExecuteAsync(
                context: context,
                argPos: argPos,
                services: null);
        }



        // MODULES
        [CheckRole("Z")]
        public class ZModule : ModuleBase<SocketCommandContext>
        {
            [Command("square")]
            [Summary("squares an int.")]
            public async Task SquareAsync(
            [Summary("The number to square.")]
            int num)
            {
                // We can also access the channel from the Command Context.
                await Context.Channel.SendMessageAsync($"{num}^2 = {Math.Pow(num, 2)}");
            }

            // ~say hello world -> hello world
            [Command("test")]
            [Summary("Echoes a message.")]
            public Task SayAsync()
                => ReplyAsync(@"| Tables        | Are           | Cool  |
| ------------- |:-------------:| -----:|
| col 3 is      | right - aligned | $1600 |
| col 2 is      | centered |   $12 |
| zebra stripes | are neat |    $1 |
");

        }

        [CheckRole("Z")]
        [Group("signup")]
        public class SignupModule : ModuleBase<SocketCommandContext>
        {

            // Typical signup:
            // @everyone
            // Day: DD/MM
            // Time: HH:SS


            [Command("create")]
            public async Task CreateSignUpAsync(string inputDate, string inputTime)
            {
                // Interpret the message format
                // DD/MM
                //string[] date = inputDate.Split('/', 2, 0);

                // HH:SS
                //string[] time = inputTime.Split(':', 2, 0);


                // Build message
                //lastSignup_header = defaultHeader;
                //lastSignup_footer = defaultFooter;

                lastSignup_date = inputDate;
                lastSignup_time = inputTime;

                // Clear old signup
                lastSignup_acceptedUsers.Clear();
                lastSignup_declinedUsers.Clear();

                string sendMessage = BuildSignupMessage();

                // Send message
                lastSignup = await signupChannel.SendMessageAsync(sendMessage);
                //lastSignup = await ReplyAsync(sendMessage);

                // Add reactions
                await lastSignup.AddReactionAsync(acceptEmoji);
                await lastSignup.AddReactionAsync(declineEmoji);

                // save last signup message.
                await File.WriteAllTextAsync(fileNameLastSignUp, lastSignup.Id.ToString());
                await File.AppendAllTextAsync(fileNameLastSignUp, '\n' + lastSignup_date);
                await File.AppendAllTextAsync(fileNameLastSignUp, '\n' + lastSignup_time);

                Console.WriteLine("Created sign-up");
            }

            [Command("printreactions")]
            public async Task PrintReactionsAsync()
            {
                string sendMessage = "";

                Console.WriteLine("Printing reactions...");

                Console.WriteLine("Accepted reactions...");
                sendMessage += "Accepted reactions...\n";

                // Accepted
                foreach (var userid in lastSignup_acceptedUsers)
                {
                    Console.WriteLine(userid.ToString());
                    sendMessage += userid.ToString() + "\n";
                }

                Console.WriteLine("Denied reactions...");
                sendMessage += "Denied reactions...\n";

                // Denied
                foreach (var userid in lastSignup_declinedUsers)
                {
                    Console.WriteLine(userid.ToString());
                    sendMessage += userid.ToString() + "\n";
                }

                // Send on discord
                await signupChannel.SendMessageAsync(sendMessage);
            }

            [Command("setheader")]
            public Task SetHeaderAsync(string newHeader)
            {
                lastSignup_header = newHeader;
                Console.WriteLine("Changed header");
                return Task.CompletedTask;
            }

            [Command("setfooter")]
            public Task SetFooterAsync(string newFooter)
            {
                lastSignup_footer = newFooter;
                Console.WriteLine("Changed footer");
                return Task.CompletedTask;
            }

        }
    }
}
