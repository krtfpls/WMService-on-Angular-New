using Application.Core;
using Application.Documents.DocumentHelpers;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Data;
using Entities;
using Entities.Documents;
using Entities.interfaces;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Documents;

public class Create
{
    public class Command : IRequest<Result<Guid>>
    {
        public NewDocument Document { get; set; }
    }

    public class CommandValidator : AbstractValidator<Command>
    {
        public CommandValidator()
        {
            RuleFor(x => x.Document.newDocument).SetValidator(new DocumentsValidator());
        }
        public class Handler : IRequestHandler<Command, Result<Guid>>
        {
            private readonly DataContext _context;
               private readonly IUserAccessor _userAccessor;
            private readonly IMapper _mapper;

            public Handler(DataContext context, IMapper mapper, IUserAccessor userAccessor)
            {
                _mapper = mapper;
                _context = context;
                 _userAccessor = userAccessor;
            }

            public async Task<Result<Guid>> Handle(Command request, CancellationToken cancellationToken)
            {
                Document doc = new Document();
                doc.User= await _context.Users.FirstOrDefaultAsync(x=> x.Id == _userAccessor.GetUserId());

//Set Customer
                if ((doc.Customer = await _context.Customers.FindAsync(request.Document!.newDocument?.CustomerId)) == null)
                    return Result<Guid>.Failure("Customer dont exist");

// Set Date
                doc.Date = request.Document.newDocument.Date;
                

// Set Type
                if ((doc.Type = await _context.DocumentTypes.FirstOrDefaultAsync(type => type.Name == request.Document.newDocument.Type)) == null)
                    return Result<Guid>.Failure("Wrong Document Type");

 // Set Number
                doc.Number = ((await _context.Documents
                                    .Where(year => year.Date.Year == doc.Date.Year)
                                    .Where(user => user.User.UserName == doc.User.UserName)
                                    .CountAsync(type => type.Type.Id == doc.Type.Id)) + 1).ToString() + "/" + DateTime.Now.Year;

//Set Lines

                try
                {
                    doc.DocumentLines = await setDocumentLines(request.Document, doc.User);
                }
                catch (InvalidLineException err)
                {
                    return Result<Guid>.Failure(err.Message);
                }

//Ready and save
                doc.Id= new Guid();
                _context.Add(doc);
//Response               
                var result = await _context.SaveChangesAsync() > 0;
                if (!result)
                    return Result<Guid>.Failure("Failed to create new Document");
                return Result<Guid>.Success(doc.Id);
            }


// Private Methods
            private async Task<IEnumerable<DocumentLine>> setDocumentLines(NewDocument document, User user)
            {
                var productLines = new List<DocumentLine>();
                var badLines = new List<DocumentLineDto>();
                
                foreach (DocumentLineDto documentLine in document.newDocument.DocumentLines)
                {
                    Product? product = await _context.Products
                                    .Include(u => u.User)
                                    .Where(u => u.User.Id == user.Id)
                                    .FirstOrDefaultAsync(x => x.Id == documentLine.ProductId);

                    if (product == null){
                        badLines.Add(documentLine);
                    }
                    else
                    {
                        productLines.Add(document.UpdateProductLine(product, documentLine.Quantity));
                        int index = productLines.Count-1;
                        
                    //    if (!checkProduct(productLines[index].Product))
                    //         badLines.Add(documentLine);
                    }
                }

                if (badLines.Count() > 0)
                {
                    string message = "Failed to add product on lines: " + System.Environment.NewLine;
                        foreach (var item in badLines)
                        {

                            message += "Position: " + item.Id + " item id: " + item.ProductId + " Qty: " + item.Quantity + ", " + System.Environment.NewLine;
                        }
                            message+= "Products don't exists or trying to add unique item with plural numbers";
                
                    throw new InvalidLineException(message);
                }
                else
                {
                    return productLines;
                }
            }

//             private bool checkProduct(Product? product)
//             {
          
// // Check if unique item and so qty on storage cannot be less than -1 and gt than 1
//                  if ((product.SerialNumber?.Length > 0) && (product.Quantity< -1 || product.Quantity > 1))
//                     return false;
//                 else
//                     return true;
//             }
        }
    };
}
