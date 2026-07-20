using Xunit;
using tktco.UdonSharpLinter;

namespace tktco.UdonSharpLinter.Tests;

public class LanguageFeatureRestrictionTests
{
    [Theory]
    [InlineData("List<int> _values = new List<int>();")]
    [InlineData("Dictionary<string, int> _values = new Dictionary<string, int>();")]
    [InlineData("HashSet<int> _values = new HashSet<int>();")]
    [InlineData("Queue<int> _values = new Queue<int>();")]
    [InlineData("Stack<int> _values = new Stack<int>();")]
    public void GenericCollectionField_ReportsError(string fieldDeclaration)
    {
        var code = $@"
using UdonSharp;
using System.Collections.Generic;

public class TestBehaviour : UdonSharpBehaviour
{{
    private {fieldDeclaration}
}}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.GenericCollectionType);
    }

    [Fact]
    public void GenericCollectionLocalVariable_ReportsError()
    {
        var code = @"
using UdonSharp;
using System.Collections.Generic;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        List<int> values = new List<int>();
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.GenericCollectionType);
    }

    [Fact]
    public void ArrayField_NoError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    private int[] _values;
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.GenericCollectionType);
    }

    [Fact]
    public void LinqUsingDirective_ReportsError()
    {
        var code = @"
using UdonSharp;
using System.Linq;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.LinqUsage);
    }

    [Fact]
    public void QualifiedLinqInvocationWithoutUsingDirective_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        int[] values = new int[] { 1, 2, 3 };
        var count = System.Linq.Enumerable.Count(values);
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.LinqUsage);
    }

    [Fact]
    public void UsingStaticLinqDirective_ReportsError()
    {
        var code = @"
using UdonSharp;
using static System.Linq.Enumerable;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.LinqUsage);
    }

    [Fact]
    public void GlobalQualifiedLinqUsingDirective_ReportsError()
    {
        var code = @"
using UdonSharp;
using global::System.Linq;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.LinqUsage);
    }

    [Fact]
    public void SystemLinqExpressionsUsingDirective_NoError()
    {
        var code = @"
using UdonSharp;
using System.Linq.Expressions;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.LinqUsage);
    }

    [Fact]
    public void NoLinqUsingDirective_NoError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        var result = Select();
    }

    private int Select()
    {
        return 1;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.LinqUsage);
    }

    [Fact]
    public void LambdaExpression_ReportsError()
    {
        var code = @"
using UdonSharp;
using System;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        Action action = () => DoSomething();
    }

    private void DoSomething() { }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.LambdaOrDelegate);
    }

    [Fact]
    public void AnonymousMethodExpression_ReportsError()
    {
        var code = @"
using UdonSharp;
using System;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        Action action = delegate { DoSomething(); };
    }

    private void DoSomething() { }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.LambdaOrDelegate);
    }

    [Fact]
    public void DelegateDeclaration_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public delegate void MyDelegate();
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.LambdaOrDelegate);
    }

    [Fact]
    public void EventFieldDeclaration_ReportsError()
    {
        var code = @"
using UdonSharp;
using System;

public class TestBehaviour : UdonSharpBehaviour
{
    public event Action OnSomething;
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.LambdaOrDelegate);
    }

    [Fact]
    public void MethodGroupAssignment_NoError()
    {
        var code = @"
using UdonSharp;
using System;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        Action action = DoSomething;
    }

    private void DoSomething() { }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.LambdaOrDelegate);
    }

    [Fact]
    public void YieldReturn_ReportsError()
    {
        var code = @"
using UdonSharp;
using System.Collections;

public class TestBehaviour : UdonSharpBehaviour
{
    private IEnumerator MyCoroutine()
    {
        yield return null;
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.CoroutineUsage);
    }

    [Fact]
    public void StartCoroutine_ReportsError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        StartCoroutine(MyCoroutine());
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.CoroutineUsage);
    }

    [Fact]
    public void NoCoroutine_NoError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        DoSomething();
    }

    private void DoSomething() { }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.CoroutineUsage);
    }

    [Fact]
    public void UnityEventAddListener_ReportsError()
    {
        var code = @"
using UdonSharp;
using UnityEngine.UI;

public class TestBehaviour : UdonSharpBehaviour
{
    public Button button;

    public void Start()
    {
        button.onClick.AddListener(OnClick);
    }

    private void OnClick() { }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.UIEventListenerRegistration);
    }

    [Fact]
    public void NoAddListener_NoError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void OnClick()
    {
        DoSomething();
    }

    private void DoSomething() { }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.UIEventListenerRegistration);
    }

    [Fact]
    public void UnrelatedAddListenerMethod_NoError()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public EventManager manager;

    public void Start()
    {
        manager.AddListener(1);
    }
}

public class EventManager
{
    public void AddListener(int id) { }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.UIEventListenerRegistration);
    }

    [Fact]
    public void GenericGetComponentUdonBehaviour_ReportsWarning()
    {
        var code = @"
using UdonSharp;
using VRC.Udon;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        var udon = GetComponent<UdonBehaviour>();
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.GenericGetComponentUdonBehaviour);
    }

    [Fact]
    public void QualifiedGenericGetComponentUdonBehaviour_ReportsWarning()
    {
        var code = @"
using UdonSharp;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        var udon = GetComponent<VRC.Udon.UdonBehaviour>();
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.Contains(errors, e => e.Code == Program.LintErrorCodes.GenericGetComponentUdonBehaviour);
    }

    [Fact]
    public void GenericGetComponentOtherType_NoError()
    {
        var code = @"
using UdonSharp;
using UnityEngine;

public class TestBehaviour : UdonSharpBehaviour
{
    public void Start()
    {
        var rb = GetComponent<Rigidbody>();
    }
}";
        var errors = Program.AnalyzeCode(code);
        Assert.DoesNotContain(errors, e => e.Code == Program.LintErrorCodes.GenericGetComponentUdonBehaviour);
    }
}
