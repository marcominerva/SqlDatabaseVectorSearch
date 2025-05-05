using FluentValidation;
using SqlDatabaseVectorSearch.Models;

namespace SqlDatabaseVectorSearch.Validators;

public class QuestionValidator : AbstractValidator<Question>
{
    public QuestionValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(4096).WithName("Question Text");
    }
}
