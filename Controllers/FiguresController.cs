using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FiguresDotStore.Controllers
{
   //1. Move interfaces and classes into spearate files
   //2. Move interfaces ans classis into separate layers according to separation of concerns
   // Data Access, Domain, Application Layer.
   
	
	internal interface IRedisClient
	{
		int Get(string type);
		void Set(string type, int current);
	}
	// Use dependency injection extract interface
	public static class FiguresStorage
	{
		// корректно сконфигурированный и готовый к использованию клиент Редиса
		private static IRedisClient RedisClient { get; }
	
		public static bool CheckIfAvailable(string type, int count)
		{
			return RedisClient.Get(type) >= count;
		}

		public static void Reserve(string type, int count)
		{
			var current = RedisClient.Get(type);

			RedisClient.Set(type, current - count);
		}
	}
        // Move this classes into ViewModel section
	// in DTOs sometimes it is OK to have all properties like that to not 
	// but in feature consider for more flexible solution to not have growing DTOs with a lot of properties for all types of Figures.
	public class Position
	{
		public string Type { get; set; }

		public float SideA { get; set; }
		public float SideB { get; set; }
		public float SideC { get; set; }

		public int Count { get; set; }
	}

	public class Cart
	{
		public List<Position> Positions { get; set; }
	}

	public class Order
	{
		// instead of having Positions property like Figures
		// we need to have property List of Order Items each one will contains info about Figure and quantity.
		
		public List<Figure> Positions { get; set; }

		public decimal GetTotal() =>
			Positions.Select(p => p switch
				{// not compiling pattern matching not all cases
					// get rid of patter matching because of violation of OCP (Open closed principle)!!!!
					// when we will add new figure we need to update this code.
					//in calculation of Total the count of figures never used!!!
					Triangle => (decimal) p.GetArea() * 1.2m,// magic numbers
					Circle => (decimal) p.GetArea() * 0.9m
				})
				.Sum();
	}
        //Bad hierarchy of classes because 
	//in base class we have propeties that not belongs to derived classes such as Circle for example shouldn't have 3 Sides only the Radius
	// so extract interface with GetArea method and get rid of this base class
	// the Validation logic is better to keep in separate classes like Validators. 
	// we will have separate Validator for each type of Figure that incapsulates validation logic
	//extract interface IFigureValidator with methid validate
	//method should return Validation result instead throwing exception.
	
	public abstract class Figure
	{
		public float SideA { get; set; }
		public float SideB { get; set; }
		public float SideC { get; set; }

		public abstract void Validate();
		public abstract double GetArea();
	}

	public class Triangle : Figure
	{
		public override void Validate()
		{
			bool CheckTriangleInequality(float a, float b, float c) => a < b + c;
			if (CheckTriangleInequality(SideA, SideB, SideC)
			    && CheckTriangleInequality(SideB, SideA, SideC)
			    && CheckTriangleInequality(SideC, SideB, SideA)) 
				return;
			throw new InvalidOperationException("Triangle restrictions not met");
		}

		public override double GetArea()
		{
			var p = (SideA + SideB + SideC) / 2;
			return Math.Sqrt(p * (p - SideA) * (p - SideB) * (p - SideC));
		}
		
	}
	
	public class Square : Figure
	{
		public override void Validate()
		{
			if (SideA < 0)
				throw new InvalidOperationException("Square restrictions not met");
			
			if (SideA != SideB)
				throw new InvalidOperationException("Square restrictions not met");
		}

		public override double GetArea() => SideA * SideA;
	}
	
	public class Circle : Figure
	{
		public override void Validate()
		{
			if (SideA < 0)
				throw new InvalidOperationException("Circle restrictions not met");
		}

		public override double GetArea() => Math.PI * SideA * SideA;
	}

	public interface IOrderStorage
	{
		// сохраняет оформленный заказ и возвращает сумму
		// in some cases it is OK to do update and get operation in same time but not recomended see CQS principle.
		Task<decimal> Save(Order order);
	}
	
	[ApiController]
	[Route("[controller]")]
	public class FiguresController : ControllerBase
	{
		private readonly ILogger<FiguresController> _logger; // logger injected but never used need to log critical logic 
		private readonly IOrderStorage _orderStorage;

		public FiguresController(ILogger<FiguresController> logger, IOrderStorage orderStorage)
		{
			_logger = logger;
			_orderStorage = orderStorage;
		}

		// хотим оформить заказ и получить в ответе его стоимость
		// the logic of workflow of plaing an order should be follow
		// 1. Validate user input
		// 2. Check the availability
		// 3. Reserve Figures
		// 4. Execute all Order creation business logic
		// 5. Place the Order.
		// 6. return result to client
		// All this workflow can be extranted into OrderService 
		// to keep cotroller as thin as possible and it will help to write clean unit tests.
		[HttpPost]
		public async Task<ActionResult> Order(Cart cart)
		{
			foreach (var position in cart.Positions)
			{
				if (!FiguresStorage.CheckIfAvailable(position.Type, position.Count))
				{
					return new BadRequestResult(); // Should return a meaningful message to the client
				}
			}
                    // here is a business logic in controller
		    // move creation of Order and Figure into Factories and Domain layer.
			var order = new Order
			{
				Positions = cart.Positions.Select(p =>
				{
					Figure figure = p.Type switch
					{
					// OCP violation again. move creation logic into separate factories
					// and register them all in container by key or use naming convention to resolve proper Figure Factory for each type of Figure
						"Circle" => new Circle(), 
						
						"Triangle" => new Triangle(),
						"Square" => new Square()
					};
					figure.SideA = p.SideA;
					figure.SideB = p.SideB;
					figure.SideC = p.SideC;
					figure.Validate(); // we need to do validation before we create order!
					return figure;
				}).ToList()
			};

			foreach (var position in cart.Positions)
			{
				FiguresStorage.Reserve(position.Type, position.Count);
			}
// it is async method but we call it syncronously. use await
			var result = _orderStorage.Save(order);

			return new OkObjectResult(result.Result);
		}
	}
}
