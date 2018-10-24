﻿using System.Collections.Generic;

namespace Util
{
	/// <summary>
	/// A class that wraps a <see cref="Queue{T}"/> and adds an immutable size. The queue is dequeued if a value is
	/// added so that the count exceeds the window size.
	/// </summary>
	public class SizedQueue<T>
	{
		private readonly int windowSize;
		
		/// <summary>
		/// Gets the values of the queue.
		/// </summary>
		public Queue<T> values { get; protected set; }
		
		/// <summary>
		/// Gets the count of <see cref="values"/>.
		/// </summary>
		public int count
		{
			get { return values.Count; }
		}

		public SizedQueue(int size)
		{
			windowSize = size;
			if (windowSize < 1)
			{
				windowSize = 1;
			}
			
			values = new Queue<T>();
		}
		
		/// <summary>
		/// Adds a value to the queue.
		/// </summary>
		/// <param name="newValue">The value to add.</param>
		public void Add(T newValue)
		{
			values.Enqueue(newValue);
			if (values.Count > windowSize)
			{
				values.Dequeue();
			}
		}

		/// <summary>
		/// Clears the queue.
		/// </summary>
		public void Clear()
		{
			values.Clear();
		}
	}
}