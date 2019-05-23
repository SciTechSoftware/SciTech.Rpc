using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Text;

namespace SciTech.Rpc.Internal
{
    internal static class RpcIlHelper
    {
        public static void EmitLdArg(ILGenerator il, int argIndex)
        {
            Debug.Assert(argIndex >= 0 && argIndex <= short.MaxValue);

            switch (argIndex)

            {
                case 0:
                    il.Emit(OpCodes.Ldarg_0);
                    break;
                case 1:
                    il.Emit(OpCodes.Ldarg_1);
                    break;
                case 2:
                    il.Emit(OpCodes.Ldarg_2);
                    break;
                case 3:
                    il.Emit(OpCodes.Ldarg_3);
                    break;
                default:
                    if (argIndex <= 255)
                    {
                        il.Emit(OpCodes.Ldarg_S, (byte)argIndex);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldarg, (short)argIndex);

                    }
                    break;
            }

        }

#pragma warning disable CA1707 // Identifiers should not contain underscores
        public static void EmitLdc_I4(ILGenerator il, int value)
        {
            //// The code below doesn't work, since I have not found a way of creating opcode from value
            //if( value >= 0 && value <= 8 )
            //{
            //    il.Emit(OpCodes.Ldc_I4_0.Value + (short)value);
            //}
            switch (value)
            {
                case 0:
                    il.Emit(OpCodes.Ldc_I4_0);
                    break;
                case 1:
                    il.Emit(OpCodes.Ldc_I4_1);
                    break;
                case 2:
                    il.Emit(OpCodes.Ldc_I4_2);
                    break;
                case 3:
                    il.Emit(OpCodes.Ldc_I4_3);
                    break;
                case 4:
                    il.Emit(OpCodes.Ldc_I4_4);
                    break;
                case 5:
                    il.Emit(OpCodes.Ldc_I4_5);
                    break;
                case 6:
                    il.Emit(OpCodes.Ldc_I4_6);
                    break;
                case 7:
                    il.Emit(OpCodes.Ldc_I4_7);
                    break;
                case 8:
                    il.Emit(OpCodes.Ldc_I4_8);
                    break;

                default:
                    if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
                    {
                        il.Emit(OpCodes.Ldc_I4_S, (sbyte)value);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldc_I4, value);

                    }
                    break;
            }

        }
#pragma warning restore CA1707 // Identifiers should not contain underscores
    }
}
