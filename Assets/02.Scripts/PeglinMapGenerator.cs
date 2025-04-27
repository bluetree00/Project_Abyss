using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeglinMapGenerator // 필요에 따라 네임스페이스를 변경하세요.
{
    // 노드 타입 열거형
    public enum NodeType
    {
        Start,
        Normal,
        End
    }

    // 노드 구조체
    public struct Node
    {
        public int Id;       // 유니크 ID (0부터 순차적으로 증가)
        public int Layer;    // 몇 번째 층인지 (0부터)
        public int Position; // 그 층에서 몇 번째 노드인지 (0부터)
        public NodeType Type; // 노드 타입

        public Node(int id, int layer, int position, NodeType type)
        {
            Id = id;
            Layer = layer;
            Position = position;
            Type = type;
        }

        // 디버깅 및 출력용 라벨 (L#-P# 형식)
        public string GetLabel() => $"L{Layer}-{Position}";

        // 디버깅 및 출력용 문자열 (n# 형식)
        public override string ToString() => $"n{Id}";
    }

    // 간선 구조체
    public struct Edge : IEquatable<Edge> // HashSet 사용 위해 IEquatable 구현
    {
        public int FromId; // 상위 노드 Id
        public int ToId;   // 하위 노드 Id

        public Edge(int from, int to)
        {
            FromId = from;
            ToId = to;
        }

        // 디버깅 및 출력용 문자열 (n# -> n# 형식)
        public override string ToString() => $"n{FromId} -> n{ToId}";

        // HashSet 에서 중복 간선 방지를 위한 비교 로직
        public bool Equals(Edge other) => FromId == other.FromId && ToId == other.ToId;
        public override int GetHashCode() => HashCode.Combine(FromId, ToId);
        public override bool Equals(object obj) => obj is Edge other && Equals(other);
    }

    // 그래프 데이터 구조
    public class Graph
    {
        public List<Node> Nodes { get; } = new List<Node>();
        // 중복 간선 방지를 위해 HashSet 사용
        public HashSet<Edge> Edges { get; } = new HashSet<Edge>();

        // ToDot() 메소드는 제거되었습니다.
        // 생성된 그래프 데이터를 사용하려면 Nodes와 Edges 컬렉션을 직접 순회해야 합니다.
    }

    // 그래프 생성기
    public static class PeglinMapGenerator
    {
        /// <summary>
        /// Peglin 맵과 유사한 구조의 결정론적 그래프를 생성합니다.
        /// </summary>
        /// <param name="layerSizes">각 레이어의 노드 수를 지정하는 배열입니다.</param>
        /// <param name="seed">노드 생성 순서 및 특정 규칙 분기에 사용될 시드값입니다. (현재 코드에서는 노드 ID에만 영향을 줍니다.)</param>
        /// <returns>생성된 그래프 데이터(노드 및 간선)를 담은 Graph 객체를 반환합니다.</returns>
        public static Graph Generate(int[] layerSizes, int seed = 123)
        {
            var graph = new Graph();
            var rnd = new Random(seed); // 간선 로직에서 직접 사용 안 함 (향후 무작위성 추가 시 활용 가능)
            var layers = new List<List<Node>>();
            int idCounter = 0;

            // 입력 유효성 검사 (최소 2개 레이어 필요, 각 레이어 크기는 1 이상)
            if (layerSizes == null || layerSizes.Length < 2)
            {
                 // 유니티나 다른 환경에서 콘솔 출력 대신 적절한 로깅 또는 예외 처리를 하세요.
                Console.WriteLine("오류: 레이어 배열은 최소 2개 이상의 크기를 가져야 합니다.");
                return graph; // 빈 그래프 반환
            }
            if (layerSizes.Any(size => size <= 0))
            {
                // 유니티나 다른 환경에서 콘솔 출력 대신 적절한 로깅 또는 예외 처리를 하세요.
                 Console.WriteLine("오류: 레이어 크기는 1 이상이어야 합니다.");
                 return graph; // 빈 그래프 반환
            }


            // 1) 노드 생성
            for (int i = 0; i < layerSizes.Length; i++)
            {
                var currentLayerNodes = new List<Node>();
                NodeType type = NodeType.Normal;
                if (i == 0) type = NodeType.Start;
                else if (i == layerSizes.Length - 1) type = NodeType.End;

                for (int pos = 0; pos < layerSizes[i]; pos++)
                {
                    var node = new Node(idCounter++, i, pos, type);
                    currentLayerNodes.Add(node);
                    graph.Nodes.Add(node);
                }

                layers.Add(currentLayerNodes);
            }

            // 2) 간선 생성 (규칙 적용 + 특정 N=M+2 케이스에서 간선 제한 무시)
            for (int i = 0; i < layers.Count - 1; i++)
            {
                var upperLayer = layers[i]; // 현재 레이어 (N)
                var lowerLayer = layers[i + 1]; // 다음 레이어 (M)
                int upperSize = upperLayer.Count;
                int lowerSize = lowerLayer.Count;

                // 다음 층 노드들의 '현재 층'으로부터 들어오는 간선 수를 기록 (기본 최대 2)
                var lowerNodeParentCounts = new Dictionary<int, int>();
                foreach (var nodeV in lowerLayer)
                {
                    lowerNodeParentCounts[nodeV.Id] = 0;
                }

                // 이 레이어 전환에서 하위 노드 간선 제한(최대 2)을 무시할지 여부를 전용 함수로 판단
                bool ignoreIncomingLimitForThisLayer = ShouldIgnoreIncomingLimit(upperSize, lowerSize);


                // 각 상위 층 노드에 대해 간선 연결 규칙 적용
                foreach (var nodeU in upperLayer) // 상위 층 노드 (Parent)
                {
                    var targetIndices = new List<int>(); // 이 상위 노드가 연결할 하위 노드들의 인덱스 목록

                    // 기존의 레이어 크기 기반 규칙 그대로 적용하여 타겟 인덱스 계산
                    // 규칙 1: 상위 레이어 노드 수 N이 1일 때
                    if (upperSize == 1)
                    {
                        targetIndices.Add(0);
                        if (lowerSize > 1)
                        {
                            targetIndices.Add(1);
                        }
                    }
                     // 규칙 2: 상위 레이어 노드 수 N이 하위 레이어 노드 수 M보다 작거나 같을 때 (1 < N <= M)
                    else if (upperSize <= lowerSize)
                    {
                        // M이 N보다 정확히 2n 큰 경우에 대한 특별 규칙 적용 (n=1)
                        // (이 규칙 자체는 제한 무시와는 별개로 대상 인덱스를 계산하는 규칙임)
                        if (lowerSize == upperSize + 2)
                        {
                            // posU * 2 와 posU * 2 + 1 위치의 노드를 연결 대상으로 삼음
                             targetIndices.Add(nodeU.Position * 2);
                             targetIndices.Add(nodeU.Position * 2 + 1);
                        }
                        else // 그 외의 N <= M 일반 규칙
                        {
                            // posU 와 posU+1 위치의 노드를 연결 대상으로 삼음
                            targetIndices.Add(nodeU.Position);
                            targetIndices.Add(nodeU.Position + 1);
                        }
                    }
                    // 규칙 3: 상위 레이어 노드 수 N이 하위 레이어 노드 수 M보다 클 때 (N > M)
                    else // upperSize > lowerSize
                    {
                         // N이 M보다 정확히 2n 큰 경우에 대한 특별 규칙 적용 (n=1)
                         // (이 규칙 자체는 제한 무시와는 별개로 대상 인덱스를 계산하는 규칙임)
                         if (upperSize == lowerSize + 2) // N = M + 2
                         {
                             // 모든 하위 레이어 노드를 연결 대상으로 삼음
                             for (int k = 0; k < lowerSize; k++)
                             {
                                 targetIndices.Add(k);
                             }
                         }
                        else // 그 외의 N > M 일반 규칙
                        {
                            if (nodeU.Position == 0) // 가장 왼쪽 노드
                            {
                                targetIndices.Add(0);
                            }
                            else if (nodeU.Position == upperSize - 1) // 가장 오른쪽 노드
                            {
                                 targetIndices.Add(lowerSize - 1);
                            }
                            else // 중간 노드 (0 < posU < upperSize - 1)
                            {
                                // 상대적 위치 계산 후 floor/ceil 인덱스를 연결 대상으로 삼음
                                double relativePos = (double)nodeU.Position / (upperSize - 1);
                                double baseIndex = relativePos * (lowerSize - 1);

                                targetIndices.Add((int)Math.Floor(baseIndex));
                                targetIndices.Add((int)Math.Ceiling(baseIndex));
                            }
                        }
                    }

                    // 계산된 타겟 인덱스들에 대해 간선 추가 시도 (제한 적용 또는 무시)
                    // 중복 인덱스 제거 및 유효 범위 확인 후 보정
                    foreach (var targetIndex in targetIndices.Distinct())
                    {
                         // Clamp를 사용하여 인덱스가 0 미만이거나 lowerSize-1을 초과하지 않도록 보정
                         int clampedTargetIndex = Math.Clamp(targetIndex, 0, lowerSize - 1);
                         var nodeV = lowerLayer[clampedTargetIndex];

                         // 이 레이어에서 제한 무시 플래그가 true이면 제한 검사 없이 추가 시도
                         if (ignoreIncomingLimitForThisLayer || lowerNodeParentCounts.GetValueOrDefault(nodeV.Id, 0) < 2)
                         {
                             var edge = new Edge(nodeU.Id, nodeV.Id);
                             // HashSet에 간선이 성공적으로 추가되면 카운트 증가 (중복 방지 포함)
                             // (제한 무시 레이어에서는 이 카운트가 실제 제한에 사용되지는 않지만,
                             // 그래프 데이터 자체의 정보로 유용할 수 있어 업데이트는 유지)
                             if (graph.Edges.Add(edge))
                             {
                                 // lowerNodeParentCounts[nodeV.Id]++; // GetValueOrDefault 사용 시 필요
                                 if (!lowerNodeParentCounts.ContainsKey(nodeV.Id)) // GetValueOrDefault 사용하지 않을 경우
                                 {
                                     lowerNodeParentCounts[nodeV.Id] = 0; // 초기화 보장
                                 }
                                 lowerNodeParentCounts[nodeV.Id]++;
                             }
                         }
                    }
                }
                 // 이 레이어 쌍에 대한 간선 생성 완료
            }

            return graph;
        }

        /// <summary>
        /// 현재 레이어 전환(upperSize -> lowerSize)에서 하위 노드의 간선 수 제한(기본 최대 2개)을 무시할지 결정합니다.
        /// 필요에 따라 다양한 레이어 크기 차이 패턴에 대한 조건을 여기에 추가하여 특정 패턴에서 제한을 해제할 수 있습니다.
        /// </summary>
        /// <param name="upperSize">현재 레이어(N)의 노드 수</param>
        /// <param name="lowerSize">다음 레이어(M)의 노드 수</param>
        /// <returns>해당 레이어 전환에서 간선 제한을 무시해야 하면 true, 아니면 false</returns>
        private static bool ShouldIgnoreIncomingLimit(int upperSize, int lowerSize)
        {
            // 현재 제한 무시 조건:
            // 1. 상위가 하위보다 많으면서 (N > M)
            // 2. 상위가 하위보다 정확히 2개 많을 때 (N = M + 2)
            // 이 조건이 동시에 만족될 때만 제한을 무시합니다.
            if (upperSize > lowerSize && upperSize == lowerSize + 2)
            {
                return true;
            }

            // --- 추후 확장 포인트 ---
            // N->N+2 (N<=M), N->N+3, N+3->N 등 다른 특정 패턴에서도 간선 제한을 무시하고
            // 해당 패턴의 규칙대로 간선을 모두 연결하고 싶다면 여기에 else if 조건을 추가하세요.
            // 예: else if (lowerSize > upperSize && lowerSize == upperSize + 2) { return true; } // M = N + 2 일 때 무시
            // 예: else if (lowerSize == upperSize + 3) { return true; } // M = N + 3 일 때 무시
            // 예: else if (upperSize == lowerSize + 3) { return true; } // N = M + 3 일 때 무시
            // ----------------------


            // 위에 정의된 어떤 조건에도 해당하지 않으면 간선 제한을 무시하지 않음 (즉, 기본 최대 2개 제한 적용)
            return false;
        }
    }

    // 런타임 테스트를 위한 Program 클래스는 제거되었습니다.
    // 유니티 스크립트나 다른 C# 콘솔 애플리케이션의 Main 메소드 등에서
    // PeglinMapGenerator.Generate(layerSizes, seed) 메소드를 호출하여 사용하세요.
}