//
// Functions.cs
//
// Author:
//       Chris Howie <me@chrishowie.com>
//
// Copyright (c) 2013 Chris Howie
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Linq;
using NUnit.Framework;

namespace Eluant.Tests
{
    [TestFixture]
    public class Functions
    {
        [Test]
        public void BasicFunction()
        {
            using (var runtime = new LuaRuntime()) {
                runtime.DoString("function basic_function(x) return x * 2 + 1 end");

                using (var fn = (LuaFunction)runtime.Globals ["basic_function"]) {
                    using (var result = fn.Call(5)) {
                        Assert.AreEqual(1, result.Count, "result.Count");
                        Assert.AreEqual(11, result [0].ToNumber(), "result[0]");
                    }
                }
            }
        }

        [Test]
        public void Callback()
        {
            int? cbValue = null;
            Action<int> callback = x => cbValue = x;

            using (var runtime = new LuaRuntime()) {
                using (var wrapper = runtime.CreateFunctionFromDelegate(callback)) {
                    runtime.Globals ["callback"] = wrapper;
                }

                runtime.DoString("callback(42)");
            }

            Assert.AreEqual(42, cbValue, "cbValue");
        }

        [Test]
        [ExpectedException(typeof(LuaException), ExpectedMessage = "$TEST$", MatchType = MessageMatch.Contains)]
        public void LuaErrorPropagation()
        {
            using (var runtime = new LuaRuntime()) {
                runtime.DoString("error('$TEST$')");
            }
        }

        [Test]
        public void LuaNonStringErrorPropagation()
        {
            using (var runtime = new LuaRuntime()) {
                try {
                    runtime.DoString("error({a = 1, b = 2, c = 3})");
                } catch (LuaException e) {
                    Assert.AreEqual(e.Message, "[LuaTable]");
                    Assert.NotNull(e.Value);
                    Assert.IsInstanceOf(typeof(LuaTable), e.Value);
                    var val = (LuaTable)e.Value;
                    Assert.AreEqual(val ["a"].ToNumber(), 1);
                    Assert.AreEqual(val ["b"].ToNumber(), 2);
                    Assert.AreEqual(val ["c"].ToNumber(), 3);
                }
            }
        }

        public static void Trace(Exception e, bool inner = false)
        {
            if (inner) Console.Write($"INNER EXCEPTION: ");
            Console.WriteLine($"{e.Message}");

            if (e is LuaException) {
                Console.WriteLine(((LuaException)e).Traceback);
            } else {
                Console.WriteLine(e.StackTrace);
            }

            if (e.InnerException != null) {
                Trace(e.InnerException, true);
            }
        }

        private void DoError(LuaRuntime runtime)
        {
            runtime.DoString("error({a = true})").Dispose();
        }

        [Test]
        public void LuaNestedErrors()
        {
            using (var runtime = new LuaRuntime()) {
                Action errorer = () => { DoError(runtime); };
                using (var func = runtime.CreateFunctionFromDelegate(errorer)) {
                    runtime.Globals ["test"] = func;
                }

                Action errorer2 = () => { runtime.DoString("test()").Dispose(); };
                using (var func = runtime.CreateFunctionFromDelegate(errorer2)) {
                    runtime.Globals ["test2"] = func;
                }

                try {
                    runtime.DoString(@"
                    function test3()
                        test2()
                    end
                    test3()");
                } catch (LuaException e) {
                    Assert.AreEqual(e.Message, "[LuaTable]");
                    Assert.IsInstanceOf<LuaTable>(e.Value);
                    var tab = (LuaTable)e.Value;
                    Assert.AreEqual(tab ["a"].ToBoolean(), true);
                    Assert.IsNull(e.InnerException);
                }
            }
        }

        [Test]
        public void LuaClrStaticMethods()
        {
            using (var runtime = new LuaRuntime()) {
                Func<LuaClrTypeObject> testex = () => { return new LuaClrTypeObject(typeof(string)); };
                using (var func = runtime.CreateFunctionFromDelegate(testex)) {
                    runtime.Globals ["extest"] = func;
                }

                runtime.DoString(@"
                    local str = extest()
                    print(str.Join(',', {'Hello', ' world!'}, 0, 2))
                ");
            }
        }

        [Test]
        public void ClrEnums()
        {
            using (var runtime = new LuaRuntime()) {
                using (var func = runtime.CreateFunctionFromDelegate(new Func<Type>(() => typeof(string)))) {
                    runtime.Globals ["get_string_type"] = func;
                }

                runtime.Globals ["StringComparison"] = new LuaClrTypeObject(typeof(StringComparison));

                runtime.DoString(@"
                    local string = get_string_type()
                    if not string.Equals('Hello!', 'Hello!', StringComparison.InvariantCulture) then
                        error('Strings aren\'t equal?')
                    end
                ").Dispose();
            }
        }

        class Something {
            public static int TheAnswer {
                get { return 42; }
            }

            public new static string ToString() {
                return "Hi";
            }

            public int A = 1;
            public int B = 2;
            public int C = 4;

            public string Test()
            {
                return "Hello, world!";
            }

            public override string ToString()
            {
                return $"A {A} B {B} C {C}";
            }
        }

        [Test]
        public void Xdef()
        {
            using (var runtime = new LuaRuntime()) {
                var inst = new Something();
                runtime.Globals ["something"] = new LuaTransparentClrObject(inst, autobind: true);

                using (var result = runtime.DoString(@"return something.GetType().TheAnswer")) {
                    Assert.AreEqual(result [0].ToNumber(), 42);
                }
            }
        }

        [Test]
        [ExpectedException(typeof(InvalidOperationException), ExpectedMessage = "Can't convert table to CLR array of System.String: Element at index 3 is a LuaNumber which is convertible to System.Double", MatchType = MessageMatch.Exact)]
        public void LuaArrayConvertionError()
        {
            using (var runtime = new LuaRuntime()) {
                runtime.Globals ["string"] = new LuaClrTypeObject(typeof(string));

                runtime.DoString(@"
                    print(string.Join(',', {'Hello', ' world!', 10}, 0, 3))
                ");
            }
        }

        [Test]
        [ExpectedException(typeof(LuaException), ExpectedMessage="$TEST$", MatchType=MessageMatch.Contains)]
        public void ClrErrorPropagation()
        {
            using (var runtime = new LuaRuntime()) {
                Action thrower = () => { throw new LuaException("$TEST$"); };

                using (var wrapper = runtime.CreateFunctionFromDelegate(thrower)) {
                    runtime.Globals["callback"] = wrapper;

                }

                runtime.DoString("callback()");
            }
        }

        [Test]
        [ExpectedException(typeof(LuaException), ExpectedMessage="Operation is not valid due to the current state of the object", MatchType=MessageMatch.Contains)]
        public void ClrExceptionPropagation()
        {
            Action thrower = () => { throw new InvalidOperationException(); };

            using (var runtime = new LuaRuntime()) {
                using (var wrapper = runtime.CreateFunctionFromDelegate(thrower)) {
                    runtime.Globals["callback"] = wrapper;
                }

                runtime.DoString("callback()");
            }
        }

        private delegate void TypeMappingTestDelegate(
            int a, ulong b, double c, string d, LuaTable e, bool f, LuaTable g);

        [Test]
        public void TypeMapping()
        {
            bool called = false;
            TypeMappingTestDelegate cb = (a, b, c, d, e, f, g) => {
                Assert.AreEqual(10, a, "a");
                Assert.AreEqual(20, b, "b");
                Assert.AreEqual(0.5, c, "c");
                Assert.AreEqual("foobar", d, "d");
                Assert.AreEqual("dingus", e["widget"].ToString(), "e");
                Assert.AreEqual(true, f, "f");
                Assert.IsNull(g, "g");

                called = true;
            };

            using (var runtime = new LuaRuntime()) {
                using (var wrapper = runtime.CreateFunctionFromDelegate(cb)) {
                    runtime.Globals["callback"] = wrapper;
                }

                runtime.DoString("callback(10, 20, 0.5, 'foobar', { widget='dingus' }, true, nil)");
            }

            Assert.IsTrue(called, "called");
        }

        [Test]
        public void HugeResultList()
        {
            var range = Enumerable.Range(1, 1000);

            Func<LuaVararg> fn = () => new LuaVararg(range.Select(i => (LuaNumber)i).Cast<LuaValue>(), true);

            using (var runtime = new LuaRuntime()) {
                using (var f = runtime.CreateFunctionFromDelegate(fn)) {
                    runtime.Globals["callback"] = f;
                }

                using (var results = runtime.DoString("return callback()")) {
                    Assert.AreEqual(range.Sum(), results.Select(i => (int)i.ToNumber().Value).Sum());
                }
            }
        }

        [Test]
        public void CallbackOnCoroutineFails()
        {
            using (var runtime = new LuaRuntime()) {
                using (var callback = runtime.CreateFunctionFromDelegate(new Action(() => { Assert.Fail("Function called."); }))) {
                    runtime.Globals["callback"] = callback;
                }

                using (var r = runtime.DoString("return coroutine.resume(coroutine.create(callback))")) {
                    Assert.IsFalse(r[0].ToBoolean(), "Call succeeded.");
                    Assert.IsTrue(r[1].ToString().EndsWith("Cannot enter the CLR from inside of a Lua coroutine."), "Error message is accurate.");
                }
            }
        }
    }
}

