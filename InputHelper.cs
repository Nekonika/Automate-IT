using System.Runtime.InteropServices;

namespace Automate_IT
{
    internal static class InputHelper
    {
        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint cInputs, Input[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Point lpPoint);

        //[DllImport("User32.dll")]
        //public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(SystemMetric smIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetMessageExtraInfo();

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        public static ushort CharToVk(char chr) =>
            (ushort)VkKeyScan(chr);

        public static void SetMousePosition(int x, int y, MousePosition mousePosition)
        {

            if (mousePosition == MousePosition.Relative)
            { 
                GetCursorPos(out Point Position);
                x += Position.X;
                y += Position.Y;
            }

            //SetCursorPos(x, y);

            Input[] Inputs = new Input[1];
            Inputs[0] = new Input()
            {
                Type = InputType.INPUT_MOUSE,
                Union = new InputUnion()
                {
                    MouseInput = new MouseInput()
                    {
                        X = mousePosition == MousePosition.Absolut ? CalculateAbsoluteCoordinateX(x) : x,
                        Y = mousePosition == MousePosition.Absolut ? CalculateAbsoluteCoordinateY(y) : y,
                        Flags = MouseInputFlags.MOUSEEVENTF_MOVE | mousePosition switch
                        {
                            MousePosition.Absolut => MouseInputFlags.MOUSEEVENTF_ABSOLUTE | MouseInputFlags.MOUSEEVENTF_VIRTUALDESK,
                            _ => MouseInputFlags.NONE
                        },
                        ExtraInfo = GetMessageExtraInfo(),
                        MouseData = 0,
                    }
                }
            };

            SendInputInternal(Inputs);
        }

        public static void MouseDown(MouseButton mouseButton)
        {
            Input[] Inputs = new Input[1];
            Inputs[0] = new Input()
            {
                Type = InputType.INPUT_MOUSE,
                Union = new InputUnion()
                {
                    MouseInput = new MouseInput()
                    {
                        X = 0,
                        Y = 0,
                        Flags = mouseButton switch
                        {
                            MouseButton.Left => MouseInputFlags.MOUSEEVENTF_LEFTDOWN,
                            MouseButton.Right => MouseInputFlags.MOUSEEVENTF_RIGHTDOWN,
                            MouseButton.Middle => MouseInputFlags.MOUSEEVENTF_MIDDLEDOWN,
                            _ => MouseInputFlags.NONE
                        },
                        ExtraInfo = GetMessageExtraInfo(),
                        MouseData = 0
                    }
                }
            };

            Input[] InputsArray = Inputs.ToArray();
            SendInputInternal(InputsArray);
        }

        public static void MouseUp(MouseButton mouseButton)
        {
            Input[] Inputs = new Input[1];
            Inputs[0] = new Input()
            {
                Type = InputType.INPUT_MOUSE,
                Union = new InputUnion()
                {
                    MouseInput = new MouseInput()
                    {
                        X = 0,
                        Y = 0,
                        Flags = MouseInputFlags.NONE | mouseButton switch
                        {
                            MouseButton.Left => MouseInputFlags.MOUSEEVENTF_LEFTUP,
                            MouseButton.Right => MouseInputFlags.MOUSEEVENTF_RIGHTUP,
                            MouseButton.Middle => MouseInputFlags.MOUSEEVENTF_MIDDLEUP,
                            _ => MouseInputFlags.NONE
                        },
                        ExtraInfo = GetMessageExtraInfo(),
                        MouseData = 0
                    }
                }
            };

            Input[] InputsArray = Inputs.ToArray();
            SendInputInternal(InputsArray);
        }

        public static void SendText(string text)
            => Array.ForEach(text.ToCharArray(), chr => { SendKey(chr); SendKey(chr, true); });

        public static void SendKey(char chr, bool release = false)
            => SendKey((KeyCode)CharToVk(chr), release, char.IsUpper(chr) || char.IsSurrogate(chr));
        public static void SendKey(KeyCode key, bool release = false, bool isUpper = false, bool useScanCode = true)
        {
            bool UseShift = isUpper && !release;
            Input[] Inputs = new Input[UseShift ? 3 : 1];

            if (UseShift)
            {
                Inputs[0] = new Input()
                {
                    Type = InputType.INPUT_KEYBOARD,
                    Union = new InputUnion()
                    {
                        KeyboardInput = new KeyboardInput()
                        {
                            VK = (ushort)(useScanCode ? 0 : KeyCode.Shift),
                            Scan = (ushort)(useScanCode ? MapVirtualKey((uint)KeyCode.Shift, (uint)KeyMapType.MAPVK_VK_TO_VSC) : 0),
                            Flags = useScanCode ? KeyboardInputFlags.KEYEVENTF_SCANCODE : KeyboardInputFlags.NONE,
                            ExtraInfo = GetMessageExtraInfo()
                        }
                    }
                };

                Inputs[2] = new Input()
                {
                    Type = InputType.INPUT_KEYBOARD,
                    Union = new InputUnion()
                    {
                        KeyboardInput = new KeyboardInput()
                        {
                            VK = (ushort)(useScanCode ? 0 : KeyCode.Shift),
                            Scan = (ushort)(useScanCode ? MapVirtualKey((uint)KeyCode.Shift, (uint)KeyMapType.MAPVK_VK_TO_VSC) : 0),
                            Flags = KeyboardInputFlags.KEYEVENTF_KEYUP | (useScanCode ? KeyboardInputFlags.KEYEVENTF_SCANCODE : KeyboardInputFlags.NONE),
                            ExtraInfo = GetMessageExtraInfo()
                        }
                    }
                };
            }

            Inputs[UseShift ? 1 : 0] = new Input()
            {
                Type = InputType.INPUT_KEYBOARD,
                Union = new InputUnion()
                {
                    KeyboardInput = new KeyboardInput()
                    {
                        VK = (ushort)(useScanCode ? 0 : key),
                        Scan = (ushort)(useScanCode ? MapVirtualKey((uint)key, (uint)KeyMapType.MAPVK_VK_TO_VSC) : 0),
                        Flags = (useScanCode ? KeyboardInputFlags.KEYEVENTF_SCANCODE : KeyboardInputFlags.NONE) | (release ? KeyboardInputFlags.KEYEVENTF_KEYUP : KeyboardInputFlags.NONE),
                        ExtraInfo = GetMessageExtraInfo()
                    }
                }
            };

            Input[] InputsArray = Inputs.ToArray();
            SendInputInternal(InputsArray);
        }

        private static void SendInputInternal(Input[] inputs)
        {
            uint uSent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(new Input()));
            if (uSent != inputs.Length)
            {
                int Error = Marshal.GetLastWin32Error();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("SendInput failed: " + Error);
                Console.ResetColor();
            }
        }

        private enum KeyMapType : uint
        { 
            MAPVK_VK_TO_VSC = 0
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct VkHelper
        {
            [FieldOffset(0)] public short Value;
            [FieldOffset(0)] public byte VkCode;
            [FieldOffset(1)] public Modifier Modifier;
        }

        private enum SystemMetric
        {
            SM_CXSCREEN = 0,
            SM_CYSCREEN = 1,
        }

        private static int CalculateAbsoluteCoordinateX(int x)
        {
            return (x * 65536) / GetSystemMetrics(SystemMetric.SM_CXSCREEN);
        }

        private static int CalculateAbsoluteCoordinateY(int y)
        {
            return (y * 65536) / GetSystemMetrics(SystemMetric.SM_CYSCREEN);
        }

        [Flags]
        private enum Modifier : byte
        {
            None = 0,          // 0b0000_0000 - 0
            Shift = 1,          // 0b0000_0001 - 1
            Ctrl = 1 << 1,     // 0b0000_0010 - 2
            Alt = 1 << 2      // 0b0000_0100 - 4
        }

        [Flags]
        private enum Flags : uint
        {
            NONE = 0,      // 0b0000_0000_0000_0000_0000_0000_0000_0000 - 0
            KEYEVENTF_EXTENDEDKEY = 1,      // 0b0000_0000_0000_0000_0000_0000_0000_0001 - 1
            KEYEVENTF_KEYUP = 1 << 1  // 0b0000_0000_0000_0000_0000_0000_0000_0010 - 2
        }

        private struct Input
        {
            public InputType Type;
            public InputUnion Union;
        }

        [Flags]
        private enum InputType : uint
        {
            INPUT_MOUSE = 0,     // 0b0000_0000_0000_0000_0000_0000_0000_0000 - 0
            INPUT_KEYBOARD = 1      // 0b0000_0000_0000_0000_0000_0000_0000_0001 - 1
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MouseInput MouseInput;
            [FieldOffset(0)]
            public KeyboardInput KeyboardInput;
        }

        private struct MouseInput
        {
            public int X;
            public int Y;
            public uint MouseData;
            public MouseInputFlags Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [Flags]
        private enum MouseInputFlags : uint
        {
            NONE = 0,                       
            MOUSEEVENTF_MOVE = 1,                   // 0b0000_0000_0000_0000_0000_0000_0000_0001 -     1
            MOUSEEVENTF_LEFTDOWN = 1 << 1,          // 0b0000_0000_0000_0000_0000_0000_0000_0010 -     2
            MOUSEEVENTF_LEFTUP = 1 << 2,            // 0b0000_0000_0000_0000_0000_0000_0000_0100 -     4
            MOUSEEVENTF_RIGHTDOWN = 1 << 3,         // 0b0000_0000_0000_0000_0000_0000_0000_1000 -     8
            MOUSEEVENTF_RIGHTUP = 1 << 4,           // 0b0000_0000_0000_0000_0000_0000_0001_0000 -    16
            MOUSEEVENTF_MIDDLEDOWN = 1 << 5,        // 0b0000_0000_0000_0000_0000_0000_0010_0000 -    32
            MOUSEEVENTF_MIDDLEUP = 1 << 6,          // 0b0000_0000_0000_0000_0000_0000_0100_0000 -    64
            //MOUSEEVENTF_XDOWN = 1 << 8,             // 0b0000_0000_0000_0000_0000_0001_0000_0000 -   256
            //MOUSEEVENTF_XUP = 1 << 9,               // 0b0000_0000_0000_0000_0000_0010_0000_0000 -   512
            //MOUSEEVENTF_WHEEL = 1 << 12,            // 0b0000_0000_0000_0000_0001_0000_0000_0000 -  4096
            //MOUSEEVENTF_HWHEEL = 1 << 13,           // 0b0000_0000_0000_0000_0010_0000_0000_0000 -  8192
            MOUSEEVENTF_MOVE_NOCOALESCE = 1 << 14,  // 0b0000_0000_0000_0000_0100_0000_0000_0000 - 16384
            MOUSEEVENTF_VIRTUALDESK = 1 << 15,      // 0b0000_0000_0000_0000_1000_0000_0000_0000 - 32768
            MOUSEEVENTF_ABSOLUTE = 1 << 16          // 0b0000_0000_0000_0001_0000_0000_0000_0000 - 65536
        }

        private struct KeyboardInput
        {
            public ushort VK;
            public ushort Scan;
            public KeyboardInputFlags Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [Flags]
        private enum KeyboardInputFlags : uint
        {
            NONE = 0,      // 0b0000_0000_0000_0000_0000_0000_0000_0000 - 0
            //KEYEVENTF_EXTENDEDKEY = 1,      // 0b0000_0000_0000_0000_0000_0000_0000_0001 - 1
            KEYEVENTF_KEYUP = 1 << 1, // 0b0000_0000_0000_0000_0000_0000_0000_0010 - 2
            //KEYEVENTF_UNICODE     = 1 << 2, // 0b0000_0000_0000_0000_0000_0000_0000_0100 - 4
            KEYEVENTF_SCANCODE    = 1 << 3, // 0b0000_0000_0000_0000_0000_0000_0000_1000 - 8
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        public enum KeyCode : byte
        {
            A = 65,
            Accept = 30,
            Add = 107,
            Application = 93,
            B = 66,
            Back = 8,
            C = 67,
            Cancel = 3,
            CapitalLock = 20,
            Clear = 12,
            Control = 17,
            Convert = 28,
            D = 68,
            Decimal = 110,
            Delete = 46,
            Divide = 111,
            Down = 40,
            E = 69,
            End = 35,
            Enter = 13,
            Escape = 27,
            Execute = 43,
            F = 70,
            F1 = 112,
            F10 = 121,
            F11 = 122,
            F12 = 123,
            F13 = 124,
            F14 = 125,
            F15 = 126,
            F16 = 127,
            F17 = 128,
            F18 = 129,
            F19 = 130,
            F2 = 113,
            F20 = 131,
            F21 = 132,
            F22 = 133,
            F23 = 134,
            F24 = 135,
            F3 = 114,
            F4 = 115,
            F5 = 116,
            F6 = 117,
            F7 = 118,
            F8 = 119,
            F9 = 120,
            Favorites = 171,
            Final = 24,
            G = 71,
            GamepadA = 195,
            GamepadB = 196,
            GamepadDPadDown = 204,
            GamepadDPadLeft = 205,
            GamepadDPadRight = 206,
            GamepadDPadUp = 203,
            GamepadLeftShoulder = 200,
            GamepadLeftThumbstickButton = 209,
            GamepadLeftThumbstickDown = 212,
            GamepadLeftThumbstickLeft = 214,
            GamepadLeftThumbstickRight = 213,
            GamepadLeftThumbstickUp = 211,
            GamepadLeftTrigger = 201,
            GamepadMenu = 207,
            GamepadRightShoulder = 199,
            GamepadRightThumbstickButton = 210,
            GamepadRightThumbstickDown = 216,
            GamepadRightThumbstickLeft = 218,
            GamepadRightThumbstickRight = 217,
            GamepadRightThumbstickUp = 215,
            GamepadRightTrigger = 202,
            GamepadView = 208,
            GamepadX = 197,
            GamepadY = 198,
            GoBack = 166,
            GoForward = 167,
            GoHome = 172,
            H = 72,
            Hangul = 21,
            Hanja = 25,
            Help = 47,
            Home = 36,
            I = 73,
            ImeOff = 26,
            ImeOn = 22,
            Insert = 45,
            J = 74,
            Junja = 23,
            K = 75,
            Kana = 21,
            Kanji = 25,
            L = 76,
            Left = 37,
            LeftButton = 1,
            LeftControl = 162,
            LeftMenu = 164,
            LeftShift = 160,
            LeftWindows = 91,
            M = 77,
            Menu = 18,
            MiddleButton = 4,
            ModeChange = 31,
            Multiply = 106,
            N = 78,
            NavigationAccept = 142,
            NavigationCancel = 143,
            NavigationDown = 139,
            NavigationLeft = 140,
            NavigationMenu = 137,
            NavigationRight = 141,
            NavigationUp = 138,
            NavigationView = 136,
            NonConvert = 29,
            None = 0,
            Number0 = 48,
            Number1 = 49,
            Number2 = 50,
            Number3 = 51,
            Number4 = 52,
            Number5 = 53,
            Number6 = 54,
            Number7 = 55,
            Number8 = 56,
            Number9 = 57,
            NumberKeyLock = 144,
            NumberPad0 = 96,
            NumberPad1 = 97,
            NumberPad2 = 98,
            NumberPad3 = 99,
            NumberPad4 = 100,
            NumberPad5 = 101,
            NumberPad6 = 102,
            NumberPad7 = 103,
            NumberPad8 = 104,
            NumberPad9 = 105,
            O = 79,
            P = 80,
            PageDown = 34,
            PageUp = 33,
            Pause = 19,
            Print = 42,
            Q = 81,
            R = 82,
            Refresh = 168,
            Right = 39,
            RightButton = 2,
            RightControl = 163,
            RightMenu = 165,
            RightShift = 161,
            RightWindows = 92,
            S = 83,
            Scroll = 145,
            Search = 170,
            Select = 41,
            Separator = 108,
            Shift = 16,
            Sleep = 95,
            Snapshot = 44,
            Space = 32,
            Stop = 169,
            Subtract = 109,
            T = 84,
            Tab = 9,
            U = 85,
            Up = 38,
            V = 86,
            W = 87,
            X = 88,
            XButton1 = 5,
            XButton2 = 6,
            Y = 89,
            Z = 90,
        }
    }
}
