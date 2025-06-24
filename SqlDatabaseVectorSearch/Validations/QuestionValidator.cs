using FluentValidation;
using SqlDatabaseVectorSearch.Models;

namespace SqlDatabaseVectorSearch.Validations;

public class QuestionValidator : AbstractValidator<Question>
{
    public QuestionValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(4096).WithName("Question Text");
    }
}
