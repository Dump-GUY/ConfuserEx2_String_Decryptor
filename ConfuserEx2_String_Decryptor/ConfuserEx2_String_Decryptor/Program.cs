using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using AsmResolver.DotNet;
using System.Linq;
using AsmResolver.PE.DotNet.Cil;
using System.Collections.Generic;
using AsmResolver.DotNet.Signatures;

namespace ConfuserEx2_String_Decryptor
{
    internal class Program
    {
        static Assembly LoadedAssembly;

        static void Main(string[] args)
        {
            Console.Title = "Dynamic ConfuserEx2 String Decryptor (ConfuserEx 1.6.0+-)";
            if (args.Length == 0)
            {
                Console.WriteLine("ConfuserEx2 String Decryptor (ConfuserEx 1.6.0+-)");
                Console.WriteLine("Use on unpacked samples (Dumped - Passing Module Constructor)");
                Console.WriteLine("Use ConfuserEx2_String_Decryptor.exe (32-bit) on 32-bit samples and ConfuserEx2_String_Decryptor.exe (64-bit) on 64-bit samples\n");
                Console.WriteLine("Usage: Drag&Drop or ConfuserEx2_String_Decryptor.exe <filepath>");
                Console.ReadKey();
                return;
            }

            if (!File.Exists(args[0]))
            {
                Console.WriteLine($"File not found: {args[0]}");
                return;
            }

            string path = Path.GetFullPath(args[0]);
            ModuleDefinition moduleDef = ModuleDefinition.FromFile(path);
            var strDecMethods = FindStrDecryptMethods(moduleDef);
            if (!strDecMethods.Any())
            {
                Console.WriteLine("String decryption methods not found!!!\nDid you unpack the sample???");
                Console.ReadKey();
                return;
            }

            LoadedAssembly = Assembly.LoadFrom(path);
            var module = LoadedAssembly.GetModules().FirstOrDefault();
            InstallHook();

            foreach (var strDecMethod in strDecMethods)
            {
                var strDecMethodRefl = module.ResolveMethod(strDecMethod.MetadataToken.ToInt32());

                foreach (TypeDefinition type in moduleDef.GetAllTypes().Where(t => t.Methods.Count > 0))
                {
                    foreach (MethodDefinition method in type.Methods.Where(m => m.CilMethodBody != null))
                    {
                        foreach (var inst in method.CilMethodBody.Instructions.Where(i => i.OpCode == CilOpCodes.Call && i.Operand is MethodSpecification))
                        {
                            if (((MethodSpecification)inst.Operand).Method.MetadataToken.ToInt32() == strDecMethod.MetadataToken.ToInt32())
                            {
                                if (inst.Operand.ToString().Contains("<System.Char[]>"))
                                {
                                    var index = method.CilMethodBody.Instructions.IndexOf(inst);
                                    for (int i = index - 1; i > index - 4; i--)
                                    {
                                        if (method.CilMethodBody.Instructions[i].OpCode == CilOpCodes.Ldc_I4)
                                        {
                                            var encValue = (int)method.CilMethodBody.Instructions[i].Operand;
                                            char[] decString = (char[])((MethodInfo)strDecMethodRefl).MakeGenericMethod(typeof(char[])).Invoke(null, new object[] { encValue });
                                            method.CilMethodBody.Instructions[i].ReplaceWith(CilOpCodes.Ldstr, new string(decString));
                                            var importer = moduleDef.DefaultImporter;
                                            var factory = moduleDef.CorLibTypeFactory;
                                            var importedMethod = factory.CorLibScope
                                                .CreateTypeReference("System", "String")
                                                .CreateMemberReference("ToCharArray", MethodSignature.CreateInstance(factory.Char.MakeSzArrayType()))
                                                .ImportWith(importer);
                                            inst.ReplaceWith(CilOpCodes.Call, importedMethod);
                                            break;
                                        }
                                    }
                                }
                                if (inst.Operand.ToString().Contains("<System.String>"))
                                {
                                    var index = method.CilMethodBody.Instructions.IndexOf(inst);
                                    for (int i = index - 1; i > index - 4; i--)
                                    {
                                        if (method.CilMethodBody.Instructions[i].OpCode == CilOpCodes.Ldc_I4)
                                        {
                                            var encValue = (int)method.CilMethodBody.Instructions[i].Operand;
                                            string decString = (string)((MethodInfo)strDecMethodRefl).MakeGenericMethod(typeof(string)).Invoke(null, new object[] { encValue });
                                            inst.ReplaceWithNop();
                                            method.CilMethodBody.Instructions[i].ReplaceWith(CilOpCodes.Ldstr, decString);
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (path.EndsWith(".exe") || path.EndsWith(".dll"))
            {
                moduleDef.Write(path.Insert(path.Length - 4, "-cleaned"));
            }
            else { moduleDef.Write(path + "-cleaned"); }
        }

        private static List<MethodDefinition> FindStrDecryptMethods(ModuleDefinition moduleDef)
        {
            List<MethodDefinition> strDecMethods = new List<MethodDefinition>();
            foreach (TypeDefinition type in moduleDef.GetAllTypes().Where(t => t.Methods.Count > 0))
            {
                foreach (MethodDefinition method in type.Methods.Where(m => m.CilMethodBody != null && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName.Equals("System.Int32")))
                {
                    if (method.CilMethodBody.Instructions.Any(inst => inst.ToString().Contains("GetExecutingAssembly()")) && method.CilMethodBody.Instructions.Any(inst => inst.ToString().Contains("GetCallingAssembly()")))
                    {
                        strDecMethods.Add(method);
                    }
                }
            }
            return strDecMethods;
        }

        private static void InstallHook()
        {
            var target = typeof(Assembly).GetMethod("GetCallingAssembly");
            if (target == null)
                throw new Exception("Could not resolve Assembly.GetCallingAssembly");

            var harmony = new Harmony("GetCallingAssembly");
            var stub = typeof(Program).GetMethod("PreFix_GetCallingAssembly");
            harmony.Patch(target, new HarmonyMethod(stub));
        }

        public static bool PreFix_GetCallingAssembly(ref Assembly __result)
        {
            __result = LoadedAssembly; // sets the result --> return value of original called method
            return false; // skip executing original GetCallingAssembly() method
        }
    }
}
