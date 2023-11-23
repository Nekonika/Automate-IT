using System.Collections.Generic;

namespace Automate_IT
{
    internal static class AitHelper
    {
        public const string AitParameterSeperator = " ";
        public const string AitCommentInline = "#";
        public const string AitText = "\"";

        private static string RemoveCommentFromLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            int FirstTextChrRaw = line.IndexOf(AitText);
            int? FirstTextChr = FirstTextChrRaw == -1 ? null : FirstTextChrRaw;

            int FirstCommentChrRaw = line.IndexOf(AitCommentInline);
            int? FirstCommentChr = FirstCommentChrRaw == -1 ? null : FirstCommentChrRaw;

            // no comment char in this line
            if (!FirstCommentChr.HasValue)
            {
                if (line.Count(chr => chr.ToString() == AitText) % 2 == 1)
                    throw new ArgumentException("A text has not been closed.", nameof(line));

                return line.Trim();
            }

            // no text char in this line
            // or comment char is before text char
            if (!FirstTextChr.HasValue || FirstCommentChr < FirstTextChr)
                return line.Split(AitCommentInline).First().Trim();

            bool IsTextFlag = false;
            for (int i = 0; i < line.Length; i++)
            {
                string CurrentChr = line[i].ToString();
                bool IsTextChr = CurrentChr == AitText;
                bool IsIsCommentChr = CurrentChr == AitCommentInline;

                // set IsTextFlag
                if (IsTextChr) IsTextFlag = !IsTextFlag;

                // return result if comment char not in text
                if (IsIsCommentChr && !IsTextFlag) return line[..i].Trim();
            }

            // text flag has not been reset
            if (IsTextFlag) throw new ArgumentException("A text has not been closed.", nameof(line));

            return line.Trim();
        }

        public static AitWorkload ParseAit(string path)
        {
            string Text = File.ReadAllText(path);
            string[] Lines = Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // remove comments
            Lines = Lines
                .Select(RemoveCommentFromLine)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Cast<string>()
                .ToArray();

            // The output is expected to be a list of
            // AIT Instructions which can be ran inline
            // without thinking about loops or procedures
            // at all.

            // commands to instructions
            List<IAitInstruction> Instructions = ResolveCommands(Lines);

            // resolve procedures
            Dictionary<string, List<IAitInstruction>> Procedures = ExtractProcedures(ref Instructions);
            ResolveExtractedProcedures(ref Procedures);

            // resolve repeats
            ValidateRepeats(ref Instructions);
            ResolveRepeats(ref Instructions);
            ResolveProcedures(ref Instructions, Procedures);

            return new AitWorkload(Instructions);
        }

        #region Command Helper
        private static List<IAitInstruction> ResolveCommands(params string[] commands)
            => commands.Select(command => CommandToInstruction(command, out _)).ToList();

        private static IAitInstruction CommandToInstruction(string command, out Instruction instruction)
        {
            string[] Components = command.Split(AitParameterSeperator);
            string Command = Components.First();
            string[] Parameters = Components.Skip(1).ToArray();

            if (Enum.TryParse(Command, true, out instruction))
            {
                return instruction switch
                {
                    Instruction.TypeText => TypeTextInstruction.Parse(Parameters),
                    Instruction.KeyPress => KeyPressInstruction.Parse(Parameters),
                    Instruction.KeyDown => KeyDownInstruction.Parse(Parameters),
                    Instruction.KeyUp => KeyUpInstruction.Parse(Parameters),
                    Instruction.MouseClick => MouseClickInstruction.Parse(Parameters),
                    Instruction.MouseDown => MouseDownInstruction.Parse(Parameters),
                    Instruction.MouseUp => MouseUpInstruction.Parse(Parameters),
                    Instruction.MouseMove => MouseMoveInstruction.Parse(Parameters),
                    Instruction.Repeat => RepeatInstruction.Parse(Parameters),
                    Instruction.EndRepeat => EndRepeatInstruction.Parse(Parameters),
                    Instruction.Procedure => ProcedureInstruction.Parse(Parameters),
                    Instruction.BeginProcedure => BeginProcedureInstruction.Parse(Parameters),
                    Instruction.EndProcedure => EndProcedureInstruction.Parse(Parameters),
                    Instruction.Delay => DelayInstruction.Parse(Parameters),
                    _ => throw new NotImplementedException()
                };
            }
            else
            {
                throw new ArgumentException($"Unrecognized command: {Command}.", nameof(command));
            }
        }
        #endregion Command Helper

        #region Procedure Helper
        // null => loop detected / string.empty => all resolved / "bla" => resolvable procedure name
        private static void ResolveExtractedProcedures(ref Dictionary<string, List<IAitInstruction>> procedures)
        {
            List<KeyValuePair<string, List<IAitInstruction>>> UnresolvedProcedures = procedures.ToList();
            Dictionary<string, List<IAitInstruction>> ResolvedProcedures = new();

            while (UnresolvedProcedures.Any())
            {
                // remove all already resolved procedures
                for (int i = 0; i < UnresolvedProcedures.Count; i++)
                {
                    KeyValuePair<string, List<IAitInstruction>> Procedure = UnresolvedProcedures[i];

                    // remove all already resolved procedures
                    if (Procedure.Value.Any(instruction => instruction.Instruction == Instruction.Procedure && !(instruction as ProcedureInstruction)!.Resolved))
                        continue;

                    ResolvedProcedures.Add(Procedure.Key, Procedure.Value);
                    UnresolvedProcedures.Remove(Procedure);
                    i--;
                }

                bool ResolvedAtLeastOneProcedure = false;

                for (int i = 0; i < UnresolvedProcedures.Count; i++)
                {
                    KeyValuePair<string, List<IAitInstruction>> Procedure = UnresolvedProcedures[i];
                    List<IAitInstruction> NewInstructions = Procedure.Value;

                    if (ResolveProcedures(ref NewInstructions, ResolvedProcedures))
                    {
                        ResolvedProcedures.Add(Procedure.Key, NewInstructions);
                        UnresolvedProcedures.Remove(Procedure);
                        i--;

                        ResolvedAtLeastOneProcedure = true;
                    }
                }

                if (!ResolvedAtLeastOneProcedure)
                    throw new ArgumentException("Could not resolve procedures. Please make sure that you did not create a loop using procedures.", nameof(procedures));
            }

            procedures = ResolvedProcedures;
        }

        private static bool ResolveProcedures(ref List<IAitInstruction> instructions, Dictionary<string, List<IAitInstruction>> extractedProcedures)
        {
            int InstructionIdx = 0;
            while (InstructionIdx < instructions.Count)
            {
                IAitInstruction AitInstruction = instructions[InstructionIdx];
                if (AitInstruction.Instruction == Instruction.Procedure)
                {
                    ProcedureInstruction AitProcedureInstruction = (AitInstruction as ProcedureInstruction)!;
                    if (!extractedProcedures.ContainsKey(AitProcedureInstruction.ProcedureName))
                    {
                        throw new ArgumentException("Could not resolve Procedure, as it has not been defined yet. This may also be due to a procedure calling itself.", nameof(instructions));
                        //return false;
                    }

                    foreach (IAitInstruction ProcedureInstruction in extractedProcedures[AitProcedureInstruction.ProcedureName])
                        instructions.Insert(++InstructionIdx, ProcedureInstruction);

                    AitProcedureInstruction.Resolved = true;
                }

                InstructionIdx++;
            }

            return true;
        }

        private static Dictionary<string, List<IAitInstruction>> ExtractProcedures(ref List<IAitInstruction> instructions)
        {
            Dictionary<string, List<IAitInstruction>> Result = new();
            List<string> CalledProcedureNames = new();

            int i = 0;
            while (i < instructions.Count)
            {
                IAitInstruction AitInstruction = instructions.ElementAt(i);
                switch (AitInstruction.Instruction)
                {
                    case Instruction.Procedure: CalledProcedureNames.Add((AitInstruction as ProcedureInstruction)!.ProcedureName); break;
                    case Instruction.BeginProcedure:
                        {
                            string ProcedureName = (AitInstruction as BeginProcedureInstruction)!.ProcedureName;
                            Result.Add(ProcedureName, ScrapeProcedureInstructions(ProcedureName, ref instructions));

                            continue;
                        }
                    case Instruction.EndProcedure: throw new ArgumentException("There seems to be at least one to many EndProcedure commands.", nameof(instructions));
                    default: break;
                }

                i++;
            }

            // check that each procedure used is actually defined somewhere
            foreach (string CalledProcedureName in CalledProcedureNames)
                if (!Result.ContainsKey(CalledProcedureName))
                    throw new ArgumentException($"You are trying to call a procedure that has not been registered: {CalledProcedureName}.", nameof(CalledProcedureName));

            return Result;
        }

        private static List<IAitInstruction> ScrapeProcedureInstructions(string procedureName, ref List<IAitInstruction> instructions)
        {
            List<IAitInstruction> Result = new();

            int ProcedureBeginsAtInstruction = instructions.FindIndex(instruction => instruction is BeginProcedureInstruction BeginProcedureInstruction && BeginProcedureInstruction.ProcedureName == procedureName);
            if (ProcedureBeginsAtInstruction == -1) throw new ArgumentException($"Trying to scrape a procedure which was not found in the instructionset: {procedureName}.", nameof(procedureName));
            if (instructions.Count <= ProcedureBeginsAtInstruction + 1) throw new ArgumentException($"BeginProcedure may not be the last command: {procedureName}.", nameof(procedureName));

            Result.Add(instructions.ElementAt(ProcedureBeginsAtInstruction));

            for (int i = ProcedureBeginsAtInstruction + 1; i < instructions.Count; i++)
            {
                IAitInstruction AitInstruction = instructions.ElementAt(i);
                switch (AitInstruction.Instruction)
                {
                    //case Instruction.Procedure: if ((AitInstruction as ProcedureInstruction)!.ProcedureName == procedureName) throw new ArgumentException($"You should not call a procedure wihtin its own definition: {procedureName}.", nameof(procedureName)); break;
                    //case Instruction.Procedure: throw new ArgumentException($"You may not use Procedure command within a procedure.", nameof(instructions));
                    case Instruction.BeginProcedure: throw new ArgumentException($"You may not define a procedure within a procedure: {procedureName}.", nameof(procedureName));
                    case Instruction.EndProcedure:
                        {
                            Result.Add(AitInstruction);
                            instructions.RemoveRange(ProcedureBeginsAtInstruction, i - ProcedureBeginsAtInstruction + 1);

                            return Result;
                        }
                    default: break;
                }

                Result.Add(AitInstruction);
            }

            throw new ArgumentException($"There is not EndProcedure command for procedure: {procedureName}.", nameof(procedureName));
        }
        #endregion Procedure Helper

        #region Repeat Helper

        private static void ValidateRepeats(ref List<IAitInstruction> instructions)
        {
            if (instructions.Select(instruction =>
            {
                if (instruction is RepeatInstruction) return 1;
                else if (instruction is EndRepeatInstruction) return -1;
                else return 0;
            }).Sum() != 0)
            {
                throw new ArgumentException("Repeat must always be closed with EndRepeat", nameof(instructions));
            }
        }

        private static bool TryResolveInnerstRepeat(ref List<IAitInstruction> instructions)
        {
            int? LastBeginRepeatIdx = null;

            for (int CurrentInstructionIdx = 0; CurrentInstructionIdx < instructions.Count; CurrentInstructionIdx++)
            {
                IAitInstruction AitInstruction = instructions.ElementAt(CurrentInstructionIdx);
                switch (AitInstruction.Instruction)
                {
                    case Instruction.Repeat:
                        {
                            if ((AitInstruction as RepeatInstruction)!.Resolved) continue;
                            LastBeginRepeatIdx = CurrentInstructionIdx;

                            break;
                        }
                    case Instruction.EndRepeat:
                        {
                            EndRepeatInstruction AitEndRepeatInstruction = (AitInstruction as EndRepeatInstruction)!;
                            if (AitEndRepeatInstruction.RepeatsRemaining.HasValue) continue;
                            if (!LastBeginRepeatIdx.HasValue) throw new ArgumentException("Command EndRepeat can not be used before the first Repeat command.");
                            if (LastBeginRepeatIdx.Value + 1 == CurrentInstructionIdx) throw new ArgumentException("Repeat must contain at lest one command.", nameof(instructions));
                            AitEndRepeatInstruction.RepeatsRemaining = 0;

                            RepeatInstruction AitRepeatInstruction = (instructions[LastBeginRepeatIdx.Value] as RepeatInstruction)!;
                            if (AitRepeatInstruction.RepeatCount <= 0) throw new ArgumentException("Repeat must have a count greater than zero.", nameof(instructions));
                            AitRepeatInstruction.Resolved = true;

                            if (AitRepeatInstruction.RepeatCount == 1) break;

                            List<IAitInstruction> RepeatInstructions = new();
                            for (int InstructionToRepeatIdx = LastBeginRepeatIdx.Value + 1; InstructionToRepeatIdx < CurrentInstructionIdx; InstructionToRepeatIdx++)
                                RepeatInstructions.Add(instructions[InstructionToRepeatIdx]);

                            RepeatInstruction ResolvedAitRepeatInstruction = new(AitRepeatInstruction) { Resolved = true };
                            int InsertAtIdx = LastBeginRepeatIdx.Value + 1;
                            for (int RepeatIdx = 0; RepeatIdx < AitRepeatInstruction.RepeatCount - 1; RepeatIdx++)
                            {
                                // repeated instructions
                                for (int InstructionToRepeatIdx = 0; InstructionToRepeatIdx < RepeatInstructions.Count; InstructionToRepeatIdx++)
                                    instructions.Insert(InsertAtIdx++, RepeatInstructions.ElementAt(InstructionToRepeatIdx));

                                // EndRepeat
                                if (RepeatIdx < AitRepeatInstruction.RepeatCount)
                                {
                                    EndRepeatInstruction InsertedAitEndRepeatInstruction = new(AitEndRepeatInstruction) { RepeatsRemaining = AitRepeatInstruction.RepeatCount - (RepeatIdx + 1) };
                                    instructions.Insert(InsertAtIdx++, InsertedAitEndRepeatInstruction);
                                }

                                // Repeat
                                instructions.Insert(InsertAtIdx++, ResolvedAitRepeatInstruction);
                            }

                            return true;
                        }
                    default: break;
                }
            }

            return false;
        }

        private static void ResolveRepeats(ref List<IAitInstruction> instructions)
        {
            while (TryResolveInnerstRepeat(ref instructions)) { }
        }
        #endregion Repeat Helper
    }

    public class AitWorkload
    {
        private readonly List<IAitInstruction> _AitInstructions;

        public IReadOnlyCollection<IAitInstruction> AitInstructions => _AitInstructions;

        public void AppendInstruction(IAitInstruction instruction)
            => _AitInstructions.Add(instruction);

        public TimeSpan Runtime => TimeSpan.FromMilliseconds(_AitInstructions.Select(inst => inst is DelayInstruction Delay ? Delay.DelayMs : 0).Sum());

        public Task Run()
        {
            foreach (IAitInstruction Instruction in _AitInstructions)
            {
                Instruction.Log();
                Instruction.Execute();
            }

            return Task.CompletedTask;
        }

        internal AitWorkload(List<IAitInstruction> instructions)
        {
            _AitInstructions = instructions;
        }
    }

    public interface IAitInstruction
    {
        public Instruction Instruction { get; }
        public object[] Parameters { get; }

        public Task Execute();
        public virtual Task<string> Log()
        {
            string Message = string.Format("{0}: {1}", Instruction, string.Join(", ", Parameters));
            Console.WriteLine(Message);

            return Task.FromResult(Message);
        }
    }

    #region TypeText
    public class TypeTextInstruction : IAitInstruction
    {
        public Instruction Instruction => Instruction.TypeText;

        private readonly List<object> _Parameters = new();
        public object[] Parameters => _Parameters.ToArray();

        public Task Execute()
        {
            InputHelper.SendText((string)Parameters.First());
            return Task.CompletedTask;
        }

        public static TypeTextInstruction Parse(params string[] args)
        {
            string Arg = string.Join(AitHelper.AitParameterSeperator, args);
            if (!Arg.StartsWith(AitHelper.AitText) || !Arg.EndsWith(AitHelper.AitText))
                throw new ArgumentException("TypeText should only be a single parameter starting and ending with '\"'.", nameof(args));

            return new TypeTextInstruction(new List<object>() { Arg[1..^1] });
        }

        private TypeTextInstruction(List<object> parameters)
        {
            _Parameters = parameters;
        }
    }
    #endregion TypeText

    #region KeyPress
    public class KeyPressInstruction : IAitInstruction
    {
        public Instruction Instruction => Instruction.KeyPress;

        private readonly List<object> _Parameters = new();
        public object[] Parameters => _Parameters.ToArray();

        public Task Execute()
        {
            InputHelper.SendKey((InputHelper.KeyCode)_Parameters.First(), false);
            InputHelper.SendKey((InputHelper.KeyCode)_Parameters.First(), true);
            return Task.CompletedTask;
        }

        public static KeyPressInstruction Parse(params string[] args)
        {
            if (args.Length != 1)
                throw new ArgumentException("KeyPress requires exactly 1 argument: vKeyName.", nameof(args));

            if (!Enum.TryParse(args.Single(), true, out InputHelper.KeyCode KeyCode))
                throw new ArgumentException($"{args.Single()} is not a valid vKeyName.", nameof(args));

            return new KeyPressInstruction(new List<object>() { KeyCode });
        }

        private KeyPressInstruction(List<object> parameters)
        {
            _Parameters = parameters;
        }
    }
    #endregion KeyPress

    #region KeyDown
    public class KeyDownInstruction : IAitInstruction
    {
        public Instruction Instruction => Instruction.KeyDown;

        private readonly List<object> _Parameters = new();
        public object[] Parameters => _Parameters.ToArray();

        public Task Execute()
        {
            InputHelper.SendKey((InputHelper.KeyCode)_Parameters.First(), false);
            return Task.CompletedTask;
        }

        public static KeyDownInstruction Parse(params string[] args)
        {
            if (args.Length != 1)
                throw new ArgumentException("KeyDown requires exactly 1 argument: vKeyName.", nameof(args));

            if (!Enum.TryParse(args.Single(), true, out InputHelper.KeyCode KeyCode))
                throw new ArgumentException($"{args.Single()} is not a valid vKeyName.", nameof(args));

            return new KeyDownInstruction(new List<object>() { KeyCode });
        }

        private KeyDownInstruction(List<object> parameters)
        {
            _Parameters = parameters;
        }
    }
    #endregion KeyDown

    #region KeyUp
    public class KeyUpInstruction : IAitInstruction
    {
        public Instruction Instruction => Instruction.KeyUp;

        private readonly List<object> _Parameters = new();
        public object[] Parameters => _Parameters.ToArray();

        public Task Execute()
        {
            InputHelper.SendKey((InputHelper.KeyCode)_Parameters.First(), true);
            return Task.CompletedTask;
        }

        public static KeyUpInstruction Parse(params string[] args)
        {
            if (args.Length != 1)
                throw new ArgumentException("KeyUp requires exactly 1 argument: vKeyName.", nameof(args));

            if (!Enum.TryParse(args.Single(), true, out InputHelper.KeyCode KeyCode))
                throw new ArgumentException($"{args.Single()} is not a valid vKeyName.", nameof(args));

            return new KeyUpInstruction(new List<object>() { KeyCode });
        }

        private KeyUpInstruction(List<object> parameters)
        {
            _Parameters = parameters;
        }
    }
    #endregion KeyUp

    #region MouseClick
    public class MouseClickInstruction : IAitInstruction
    {
        public Instruction Instruction => Instruction.MouseClick;

        private readonly List<object> _Parameters = new();
        public object[] Parameters => _Parameters.ToArray();

        public Task Execute()
        {
            InputHelper.MouseDown((MouseButton)_Parameters.Single());
            InputHelper.MouseUp((MouseButton)_Parameters.Single());
            return Task.CompletedTask;
        }

        public static MouseClickInstruction Parse(params string[] args)
        {
            if (args.Length != 1)
                throw new ArgumentException("MouseClick requires exactly 1 argument: MouseButton.", nameof(args));

            if (!Enum.TryParse(args.Single(), true, out MouseButton MouseButton))
                throw new ArgumentException($"{args.Single()} is not a valid MouseButton.", nameof(args));

            return new MouseClickInstruction(new List<object>() { MouseButton });
        }

        private MouseClickInstruction(List<object> parameters)
        {
            _Parameters = parameters;
        }
    }
    #endregion MouseClick

    #region MouseDown
    public class MouseDownInstruction : IAitInstruction
    {
        public Instruction Instruction => Instruction.MouseDown;

        private readonly List<object> _Parameters = new();
        public object[] Parameters => _Parameters.ToArray();

        public Task Execute()
        {
            InputHelper.MouseDown((MouseButton)_Parameters.Single());
            return Task.CompletedTask;
        }

        public static MouseDownInstruction Parse(params string[] args)
        {
            if (args.Length != 1)
                throw new ArgumentException("MouseDown requires exactly 1 argument: MouseButton.", nameof(args));

            if (!Enum.TryParse(args.Single(), true, out MouseButton MouseButton))
                throw new ArgumentException($"{args.Single()} is not a valid MouseButton.", nameof(args));

            return new MouseDownInstruction(new List<object>() { MouseButton });
        }

        private MouseDownInstruction(List<object> parameters)
        {
            _Parameters = parameters;
        }
    }
    #endregion MouseDown

    #region MouseUp
    public class MouseUpInstruction : IAitInstruction
    {
        public Instruction Instruction => Instruction.MouseUp;

        private readonly List<object> _Parameters = new();
        public object[] Parameters => _Parameters.ToArray();

        public Task Execute()
        {
            InputHelper.MouseUp((MouseButton)_Parameters.Single());
            return Task.CompletedTask;
        }

        public static MouseUpInstruction Parse(params string[] args)
        {
            if (args.Length != 1)
                throw new ArgumentException("MouseUp requires exactly 1 argument: MouseButton.", nameof(args));

            if (!Enum.TryParse(args.Single(), true, out MouseButton MouseButton))
                throw new ArgumentException($"{args.Single()} is not a valid MouseButton.", nameof(args));

            return new MouseUpInstruction(new List<object>() { MouseButton });
        }

        private MouseUpInstruction(List<object> parameters)
        {
            _Parameters = parameters;
        }
    }
    #endregion MouseUp

    #region MouseMove
    public class MouseMoveInstruction : IAitInstruction
    {
        public Instruction Instruction => Instruction.MouseMove;

        private readonly List<object> _Parameters = new();
        public object[] Parameters => _Parameters.ToArray();

        public Task Execute()
        {
            InputHelper.SetMousePosition((int)_Parameters[0], (int)_Parameters[1], (MousePosition)_Parameters[2]);
            return Task.CompletedTask;
        }

        public static MouseMoveInstruction Parse(params string[] args)
        {
            if (args.Length != 3)
                throw new ArgumentException("MouseUp requires exactly 3 arguments: X, Y, MousePosition.", nameof(args));

            if (!int.TryParse(args[0], out int X) || !int.TryParse(args[1], out int Y))
                throw new ArgumentException("Could not parse mouse position.", nameof(args));

            if (!Enum.TryParse(args[2], true, out MousePosition MousePosition))
                throw new ArgumentException($"{args[2]} is not a valid MousePosition.", nameof(args));

            return new MouseMoveInstruction(new List<object>() { X, Y, MousePosition });
        }

        private MouseMoveInstruction(List<object> parameters)
        {
            _Parameters = parameters;
        }
    }
    #endregion MouseMove

    #region Repeat
    public class RepeatInstruction : IAitInstruction
    {
        public Instruction Instruction => Instruction.Repeat;

        private readonly List<object> _Parameters = new();
        public object[] Parameters => _Parameters.ToArray();

        internal bool Resolved = false;
        public int RepeatCount => (int)_Parameters.Single();

        public Task Execute()
        {
            return Task.CompletedTask;
        }

        public Task<string> Log()
        {
            string Message = string.Format("{0}: {1}", Instruction, string.Join(", ", Parameters));

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine();
            Console.WriteLine(Message);
            Console.ResetColor();

            return Task.FromResult(Message);
        }

        public static RepeatInstruction Parse(params string[] args)
        {
            if (args.Length != 1)
                throw new ArgumentException("Repeat requires exactly 1 argument: amount.", nameof(args));

            if (!int.TryParse(args[0], out int Amount))
                throw new ArgumentException("Could not parse amount.", nameof(args));

            return new RepeatInstruction(new List<object>() { Amount });
        }

        private RepeatInstruction(List<object> parameters)
        {
            _Parameters = parameters;
        }

        public RepeatInstruction(RepeatInstruction repeatInstruction)
        {
            _Parameters = repeatInstruction._Parameters;
        }
    }
    #endregion Repeat

    #region EndRepeat
    public class EndRepeatInstruction : IAitInstruction
    {
        public Instruction Instruction => Instruction.EndRepeat;

        private readonly List<object> _Parameters = new();
        public object[] Parameters => _Parameters.ToArray();

        internal int? RepeatsRemaining = null;

        public Task Execute()
        {
            return Task.CompletedTask;
        }

        public Task<string> Log()
        {
            string Message = string.Format("{0}: {1}", Instruction, RepeatsRemaining == 0 ? "End Repeat" : $"Repeats remaining {RepeatsRemaining}");

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(Message);
            Console.WriteLine();
            Console.ResetColor();

            return Task.FromResult(Message);
        }

        public static EndRepeatInstruction Parse(params string[] args)
        {
            if (args.Length != 0)
                throw new ArgumentException("EndRepeat does not need any arguments.", nameof(args));

            return new EndRepeatInstruction(new List<object>() { });
        }

        private EndRepeatInstruction(List<object> parameters)
        {
            _Parameters = parameters;
        }

        public EndRepeatInstruction(EndRepeatInstruction endRepeatInstruction)
        {
            _Parameters = endRepeatInstruction._Parameters;
        }
    }
    #endregion EndRepeatInstruction

    #region Procedure
    public class ProcedureInstruction : IAitInstruction
    {
        public Instruction Instruction => Instruction.Procedure;

        private readonly List<object> _Parameters = new();
        public object[] Parameters => _Parameters.ToArray();

        public string ProcedureName => (string)_Parameters.Single();
        internal bool Resolved = false;

        public Task Execute()
        {
            return Task.CompletedTask;
        }

        public static ProcedureInstruction Parse(params string[] args)
        {
            if (args.Length != 1)
                throw new ArgumentException("Procedure requires exactly 1 argument: name.", nameof(args));

            return new ProcedureInstruction(new List<object>() { args.Single() });
        }

        private ProcedureInstruction(List<object> parameters)
        {
            _Parameters = parameters;
        }
    }
    #endregion BeginProcedure

    #region BeginProcedure
    public class BeginProcedureInstruction : IAitInstruction
    {
        public Instruction Instruction => Instruction.BeginProcedure;

        private readonly List<object> _Parameters = new();
        public object[] Parameters => _Parameters.ToArray();

        public string ProcedureName => (string)_Parameters.Single();

        public Task Execute()
        {
            return Task.CompletedTask;
        }

        public Task<string> Log()
        {
            string Message = string.Format("{0}: {1}", Instruction, string.Join(", ", Parameters));

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine();
            Console.WriteLine(Message);
            Console.ResetColor();

            return Task.FromResult(Message);
        }

        public static BeginProcedureInstruction Parse(params string[] args)
        {
            if (args.Length != 1)
                throw new ArgumentException("BeginProcedure requires exactly 1 argument: name.", nameof(args));

            return new BeginProcedureInstruction(new List<object>() { args.Single() });
        }

        private BeginProcedureInstruction(List<object> parameters)
        {
            _Parameters = parameters;
        }
    }
    #endregion BeginProcedure

    #region EndProcedure
    public class EndProcedureInstruction : IAitInstruction
    {
        public Instruction Instruction => Instruction.EndProcedure;

        private readonly List<object> _Parameters = new();
        public object[] Parameters => _Parameters.ToArray();

        public Task Execute()
        {
            return Task.CompletedTask;
        }

        public Task<string> Log()
        {
            string Message = string.Format("{0}: {1}", Instruction, string.Join(", ", Parameters));

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine(Message);
            Console.WriteLine();
            Console.ResetColor();

            return Task.FromResult(Message);
        }

        public static EndProcedureInstruction Parse(params string[] args)
        {
            if (args.Length != 0)
                throw new ArgumentException("EndProcedure does not require any arguments.", nameof(args));

            return new EndProcedureInstruction(new List<object>() { });
        }

        private EndProcedureInstruction(List<object> parameters)
        {
            _Parameters = parameters;
        }
    }
    #endregion EndProcedureInstruction

    #region Delay
    public class DelayInstruction : IAitInstruction
    {
        public Instruction Instruction => Instruction.Delay;

        private readonly List<object> _Parameters = new();
        public object[] Parameters => _Parameters.ToArray();

        public int DelayMs => (int)_Parameters.Single();

        public Task Execute()
        {
            Task.Delay((int)_Parameters.Single()).Wait();
            return Task.CompletedTask;
        }

        public static DelayInstruction Parse(params string[] args)
        {
            if (args.Length != 1)
                throw new ArgumentException("Delay requires exactly 1 argument: delay.", nameof(args));

            if (!int.TryParse(args[0], out int Delay))
                throw new ArgumentException("Could not parse delay.", nameof(args));

            return new DelayInstruction(new List<object>() { Delay });
        }

        private DelayInstruction(List<object> parameters)
        {
            _Parameters = parameters;
        }
    }
    #endregion Delay

    public enum Instruction
    {
        TypeText,           // enters the given text
        KeyPress,           // presses and releases the specified key
        KeyDown,            // presses the specified key
        KeyUp,              // releases the specified key
        MouseClick,         // clicks the specified mouse button
        MouseDown,          // holds down the specified mouse button
        MouseUp,            // releases the specified mouse button
        MouseMove,          // moves the cursor to the specified position
        Repeat,             // defines the beginning of a repeating section - repeats by the amount specified
        EndRepeat,          // defines the end of a repeating section
        Procedure,          // starts a named procedure
        BeginProcedure,     // defines the biginning of a procedure and stores it using the specified name
        EndProcedure,       // defines the end of a procedure
        Delay               // waits for the specified amount of ms
    }

    public enum MousePosition
    {
        Absolut,
        Relative
    }

    public enum MouseButton
    {
        Left,
        Right,
        Middle,
        //WheelUp,
        //WheelDown,
        //Mouse4,
        //Mouse5,
    }
}
