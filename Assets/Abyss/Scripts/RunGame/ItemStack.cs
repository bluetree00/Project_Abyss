using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System;

namespace Game.Items
{
    /// <summary>
    /// 아이템 종류(ItemId) + 수량을 표현하는 값 타입
    /// - 런 보상, 인벤 반영, 저장/로드에 사용
    /// - 실제 아이템 데이터(ItemData)는 ItemId로 테이블에서 조회
    /// </summary>
    [Serializable]
    public readonly struct ItemStack : IEquatable<ItemStack>
    {
        /// <summary>
        /// 테이블에 정의된 아이템 고유 ID
        /// (enum 또는 int 기반 ItemId를 사용)
        /// </summary>
        public ItemId ItemId { get; }

        /// <summary>
        /// 아이템 수량 (항상 0 이상)
        /// </summary>
        public int Count { get; }

        public bool IsEmpty => Count <= 0 || ItemId.Equals(default);

        public ItemStack(ItemId itemId, int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "ItemStack count cannot be negative.");

            ItemId = itemId;
            Count = count;
        }

        /// <summary>
        /// 수량 증가 (불변 구조이므로 새 ItemStack 반환)
        /// </summary>
        public ItemStack Add(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));

            return new ItemStack(ItemId, Count + amount);
        }

        /// <summary>
        /// 수량 감소 (0 미만 방지)
        /// </summary>
        public ItemStack Subtract(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));

            int next = Count - amount;
            if (next < 0)
                throw new InvalidOperationException("ItemStack count cannot go below zero.");

            return new ItemStack(ItemId, next);
        }

        // --------------------
        // Equality
        // --------------------
        public bool Equals(ItemStack other)
        {
            return ItemId.Equals(other.ItemId) && Count == other.Count;
        }

        public override bool Equals(object obj)
        {
            return obj is ItemStack other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (ItemId.GetHashCode() * 397) ^ Count;
            }
        }

        public static bool operator ==(ItemStack left, ItemStack right) => left.Equals(right);
        public static bool operator !=(ItemStack left, ItemStack right) => !left.Equals(right);

        public override string ToString()
        {
            return $"{ItemId} x{Count}";
        }

        /// <summary>
        /// 편의용: 빈 스택
        /// </summary>
        public static ItemStack Empty => new ItemStack(default, 0);
    }
}
